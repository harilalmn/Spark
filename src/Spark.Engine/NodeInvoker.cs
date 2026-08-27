using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

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

        if (method.ReturnType == typeof(void) && !HasOutParameter(parameters))
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

        Expression call = Expression.Call(instance, method, callArguments);

        return Compile(call, method.ReturnType, outputVariables, arguments);
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
        ref int argumentIndex)
    {
        List<ParameterExpression> outputVariables = [];
        List<Expression> callArguments = [];

        foreach (ParameterInfo parameter in parameters)
        {
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
        ParameterExpression arguments)
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

        return Expression.Lambda<NodeInvocation>(Expression.Block(locals, statements), arguments).Compile();
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
