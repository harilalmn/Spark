using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Spark.Engine;

/// <summary>
/// Compiles a <see cref="NodeInvocation"/> from a method or constructor using expression trees.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is never <c>MethodInfo.Invoke</c>, and that is not a preference.</b> Replication runs the
/// underlying member once per element, so a graph that fans a node out over a hundred thousand
/// points calls it a hundred thousand times. The reflection path is fifty to a hundred times slower
/// per call, which does not make lacing slow — it makes it unusable, and a user would experience it
/// as "Spark cannot handle a real model" rather than as a performance note.
/// </para>
/// <para>
/// The compiled shape is one delegate that unpacks an <c>object[]</c>, casts each slot to the
/// declared parameter type, calls the member directly, and packs the return value and any
/// <c>out</c> parameters back into an <c>object[]</c>. Casting is a real cast, so a value that
/// slipped past marshalling throws an <see cref="InvalidCastException"/> at the leaf, where
/// per-element isolation can catch it.
/// </para>
/// <para>
/// <c>out</c> parameters become extra output ports after the return value, which is how a node
/// gets more than one output. <c>void</c> members produce only their <c>out</c> parameters.
/// </para>
/// </remarks>
public static class NodeInvoker
{
    /// <summary>
    /// What a node built from this member actually produces: the awaited result for an
    /// asynchronous member, and the return type itself for every other one (<c>E5-T3</c>).
    /// </summary>
    /// <param name="returnType">The member's declared return type.</param>
    /// <returns>
    /// <c>T</c> for <c>Task&lt;T&gt;</c> and <c>ValueTask&lt;T&gt;</c>, <see langword="void"/> for
    /// bare <c>Task</c> and <c>ValueTask</c>, and <paramref name="returnType"/> otherwise.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="returnType"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// <b>A node is a function from values to values, so an asynchronous member is awaited and its
    /// result is what the port carries.</b> Without this the port's type is <c>Task&lt;double&gt;</c>
    /// and its value is a task object — which nothing downstream can add, draw or serialise, and
    /// which no user would recognise as a mistake in Spark rather than in the package they
    /// imported. The importer imports whatever assembly it is pointed at, with no cooperation from
    /// its author, so it reaches this the first time somebody imports a library with an
    /// <c>async</c> member in it.
    /// </para>
    /// <para>
    /// <b><c>ValueTask</c> is handled beside <c>Task</c> rather than refused</b>, because refusing
    /// it would leave exactly the same defect wearing a different name in every library written
    /// since 2018.
    /// </para>
    /// <para>
    /// <b>Bare <c>Task</c> unwraps to <see langword="void"/></b>, and so is refused by the same
    /// rule that refuses <c>void</c>: it produces no value a graph can carry. An <c>async</c>
    /// method returning <c>Task</c> is a side effect, and a side effect is declared, not inferred.
    /// </para>
    /// </remarks>
    public static Type ResultTypeOf(Type returnType)
    {
        ArgumentNullException.ThrowIfNull(returnType);

        if (returnType == typeof(Task) || returnType == typeof(ValueTask))
        {
            return typeof(void);
        }

        if (returnType.IsGenericType)
        {
            Type definition = returnType.GetGenericTypeDefinition();

            if (definition == typeof(Task<>) || definition == typeof(ValueTask<>))
            {
                return returnType.GetGenericArguments()[0];
            }
        }

        return returnType;
    }

    /// <summary>Whether a member has to be awaited before its value can be used.</summary>
    /// <param name="returnType">The member's declared return type.</param>
    /// <returns>True for the four awaitable shapes this importer understands.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="returnType"/> is null.</exception>
    /// <remarks>
    /// <b>The four shapes, not a duck-typed check for a <c>GetAwaiter</c> method.</b> A general
    /// awaitable is anything with the right method pattern, including types whose awaiter is not
    /// safe to block on; these four are, and widening it is a decision to take when something needs
    /// it rather than in anticipation.
    /// </remarks>
    public static bool IsAwaitable(Type returnType) => ResultTypeOf(returnType) != returnType;

    /// <summary>
    /// Wraps a call in <c>GetAwaiter().GetResult()</c> when it returns an awaitable.
    /// </summary>
    /// <param name="call">The call expression.</param>
    /// <returns>The call, or the call awaited.</returns>
    /// <remarks>
    /// <b>The await blocks, deliberately.</b> <c>GraphEvaluator</c> is synchronous — a node is a
    /// function from values to values, and making one node asynchronous would make the whole
    /// evaluator asynchronous for the benefit of members that mostly are not. Blocking is safe
    /// here for a specific reason and not a general one: evaluation runs on a worker thread with no
    /// synchronisation context, so there is no context for the continuation to be posted back to
    /// and nothing to dead-lock against. A node that ran on the UI thread could not do this.
    /// </remarks>
    private static Expression Await(Expression call) =>
        IsAwaitable(call.Type)
            ? Expression.Call(
                Expression.Call(call, call.Type.GetMethod("GetAwaiter")!),
                "GetResult",
                typeArguments: null)
            : call;

    /// <summary>
    /// Compiles an invoker for a method. An instance method takes its receiver as input port 0.
    /// </summary>
    /// <param name="method">The method.</param>
    /// <returns>The compiled invoker.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="method"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The method is generic and not closed, or it produces nothing at all — neither a return value
    /// nor an <c>out</c> parameter.
    /// </exception>
    public static NodeInvocation ForMethod(MethodInfo method)
    {
        ArgumentNullException.ThrowIfNull(method);

        if (method.ContainsGenericParameters)
        {
            throw new ArgumentException(
                $"'{method.Name}' is an open generic method. Close it over concrete types before compiling an invoker.",
                nameof(method));
        }

        ParameterInfo[] parameters = method.GetParameters();

        Type produces = ResultTypeOf(method.ReturnType);

        if (produces == typeof(void) && !HasOutParameter(parameters))
        {
            throw new ArgumentException(
                $"'{method.Name}' returns void and has no out parameters, so it produces no value a graph can carry.",
                nameof(method));
        }

        ParameterExpression arguments = Expression.Parameter(typeof(object[]), "arguments");
        int argumentIndex = 0;

        Expression? instance = null;
        if (!method.IsStatic)
        {
            instance = Expression.Convert(
                Expression.ArrayIndex(arguments, Expression.Constant(argumentIndex++)),
                method.DeclaringType ?? throw new ArgumentException(
                    $"'{method.Name}' has no declaring type.", nameof(method)));
        }

        (List<ParameterExpression> outputVariables, List<Expression> callArguments) =
            BuildCallArguments(parameters, arguments, ref argumentIndex);

        Expression call = Await(Expression.Call(instance, method, callArguments));

        return Compile(call, produces, outputVariables, arguments);
    }

    /// <summary>
    /// Whether a method declares a <see cref="CancellationToken"/> for the evaluation to fill (`E3-T12`).
    /// </summary>
    /// <param name="method">The method.</param>
    /// <returns>True when one of its parameters is a token.</returns>
    public static bool TakesCancellation(MethodInfo method)
    {
        ArgumentNullException.ThrowIfNull(method);

        foreach (ParameterInfo parameter in method.GetParameters())
        {
            if (parameter.ParameterType == typeof(CancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Compiles an invoker for a method that declares a <see cref="CancellationToken"/>, which is
    /// handed the evaluation's token (`E3-T12`).
    /// </summary>
    /// <param name="method">The method.</param>
    /// <returns>The compiled invoker.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="method"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The method is open generic, or produces nothing.</exception>
    /// <remarks>
    /// <b>The token takes no port and no argument slot.</b> It is the evaluation's, not the graph's:
    /// nothing wires into it, nothing is saved for it, and the node's key — its name, and on an
    /// overload collision its port names — does not change because a method started accepting one.
    /// </remarks>
    public static CancellableNodeInvocation ForCancellableMethod(MethodInfo method)
    {
        ArgumentNullException.ThrowIfNull(method);

        if (method.ContainsGenericParameters)
        {
            throw new ArgumentException(
                $"'{method.Name}' is an open generic method. Close it over concrete types before compiling an invoker.",
                nameof(method));
        }

        ParameterInfo[] parameters = method.GetParameters();

        Type produces = ResultTypeOf(method.ReturnType);

        if (produces == typeof(void) && !HasOutParameter(parameters))
        {
            throw new ArgumentException(
                $"'{method.Name}' returns void and has no out parameters, so it produces no value a graph can carry.",
                nameof(method));
        }

        ParameterExpression arguments = Expression.Parameter(typeof(object[]), "arguments");
        ParameterExpression token = Expression.Parameter(typeof(CancellationToken), "cancellationToken");
        int argumentIndex = 0;

        Expression? instance = null;
        if (!method.IsStatic)
        {
            instance = Expression.Convert(
                Expression.ArrayIndex(arguments, Expression.Constant(argumentIndex++)),
                method.DeclaringType ?? throw new ArgumentException(
                    $"'{method.Name}' has no declaring type.", nameof(method)));
        }

        (List<ParameterExpression> outputVariables, List<Expression> callArguments) =
            BuildCallArguments(parameters, arguments, ref argumentIndex, token);

        Expression call = Await(Expression.Call(instance, method, callArguments));

        return Compile<CancellableNodeInvocation>(call, produces, outputVariables, arguments, token);
    }

    /// <summary>
    /// Compiles an invoker for a constructor. Its parameters are the input ports and the
    /// constructed value is output port 0.
    /// </summary>
    /// <param name="constructor">The constructor.</param>
    /// <returns>The compiled invoker.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="constructor"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The declaring type is generic and not closed.</exception>
    public static NodeInvocation ForConstructor(ConstructorInfo constructor)
    {
        ArgumentNullException.ThrowIfNull(constructor);

        if (constructor.ContainsGenericParameters)
        {
            throw new ArgumentException(
                $"'{constructor.DeclaringType?.Name}' is an open generic type. Close it before compiling an invoker.",
                nameof(constructor));
        }

        ParameterExpression arguments = Expression.Parameter(typeof(object[]), "arguments");
        int argumentIndex = 0;

        (List<ParameterExpression> outputVariables, List<Expression> callArguments) =
            BuildCallArguments(constructor.GetParameters(), arguments, ref argumentIndex);

        Expression call = Expression.New(constructor, callArguments);

        return Compile(call, constructor.DeclaringType!, outputVariables, arguments);
    }

    private static (List<ParameterExpression> OutputVariables, List<Expression> CallArguments) BuildCallArguments(
        ParameterInfo[] parameters,
        ParameterExpression arguments,
        ref int argumentIndex,
        Expression? token = null)
    {
        List<ParameterExpression> outputVariables = [];
        List<Expression> callArguments = [];

        foreach (ParameterInfo parameter in parameters)
        {
            // `E3-T12`: the evaluation's token, where one was supplied, and no token at all where it
            // was not. Either way it takes no argument slot, because it has no port.
            if (parameter.ParameterType == typeof(CancellationToken))
            {
                callArguments.Add(token ?? Expression.Default(typeof(CancellationToken)));
                continue;
            }

            if (parameter.IsOut)
            {
                ParameterExpression variable = Expression.Variable(
                    parameter.ParameterType.GetElementType()!, parameter.Name ?? $"out{parameter.Position}");
                outputVariables.Add(variable);
                callArguments.Add(variable);
                continue;
            }

            Type parameterType = parameter.ParameterType;
            if (parameterType.IsByRef)
            {
                parameterType = parameterType.GetElementType()!;
            }

            callArguments.Add(Expression.Convert(
                Expression.ArrayIndex(arguments, Expression.Constant(argumentIndex++)), parameterType));
        }

        return (outputVariables, callArguments);
    }

    private static NodeInvocation Compile(
        Expression call,
        Type returnType,
        List<ParameterExpression> outputVariables,
        ParameterExpression arguments) =>
        Compile<NodeInvocation>(call, returnType, outputVariables, arguments);

    private static TDelegate Compile<TDelegate>(
        Expression call,
        Type returnType,
        List<ParameterExpression> outputVariables,
        params ParameterExpression[] lambdaParameters)
        where TDelegate : Delegate
    {
        List<ParameterExpression> locals = [.. outputVariables];
        List<Expression> statements = [];
        List<Expression> results = [];

        if (returnType == typeof(void))
        {
            statements.Add(call);
        }
        else
        {
            ParameterExpression result = Expression.Variable(returnType, "result");
            locals.Insert(0, result);
            statements.Add(Expression.Assign(result, call));
            results.Add(Expression.Convert(result, typeof(object)));
        }

        foreach (ParameterExpression variable in outputVariables)
        {
            results.Add(Expression.Convert(variable, typeof(object)));
        }

        statements.Add(Expression.NewArrayInit(typeof(object), results));

        return Expression.Lambda<TDelegate>(Expression.Block(locals, statements), lambdaParameters).Compile();
    }

    private static bool HasOutParameter(ParameterInfo[] parameters)
    {
        foreach (ParameterInfo parameter in parameters)
        {
            if (parameter.IsOut)
            {
                return true;
            }
        }

        return false;
    }
}
