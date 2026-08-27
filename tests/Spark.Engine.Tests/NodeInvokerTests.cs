using System;
using System.Reflection;
using Spark.Engine;

namespace Spark.Engine.Tests;

/// <summary>
/// The compiled invocation path, driven from hand-built definitions. The reflection importer that
/// will produce these definitions does not exist yet; the delegate it will depend on does, and this
/// is what proves it.
/// </summary>
public sealed class NodeInvokerTests
{
    /// <summary>A receiver for the instance-method case.</summary>
    public sealed class Counter
    {
        public Counter(int start) => Value = start;

        public int Value { get; }

        public int Plus(int amount) => Value + amount;
    }

    /// <summary>A static method's return value becomes output port 0.</summary>
    [Fact]
    public void AStaticMethodProducesOneOutput()
    {
        NodeInvocation invoke = NodeInvoker.ForMethod(Method(typeof(LacingMembers), nameof(LacingMembers.Add)));

        object?[] outputs = invoke([3.0, 4.0]);

        Assert.Single(outputs);
        Assert.Equal(7.0, outputs[0]);
    }

    /// <summary>An instance method takes its receiver as input port 0.</summary>
    [Fact]
    public void AnInstanceMethodTakesItsReceiverAsPortZero()
    {
        NodeInvocation invoke = NodeInvoker.ForMethod(Method(typeof(Counter), nameof(Counter.Plus)));

        object?[] outputs = invoke([new Counter(10), 5]);

        Assert.Equal(15, outputs[0]);
    }

    /// <summary>A constructor's result becomes output port 0.</summary>
    [Fact]
    public void AConstructorProducesTheConstructedValue()
    {
        NodeInvocation invoke = NodeInvoker.ForConstructor(typeof(Counter).GetConstructor([typeof(int)])!);

        object?[] outputs = invoke([7]);

        Assert.Equal(7, Assert.IsType<Counter>(outputs[0]).Value);
    }

    /// <summary>
    /// <c>out</c> parameters become extra output ports after the return value, which is how a node
    /// gets more than one output at all.
    /// </summary>
    [Fact]
    public void OutParametersBecomeExtraOutputPortsInOrder()
    {
        NodeInvocation invoke = NodeInvoker.ForMethod(Method(typeof(LacingMembers), nameof(LacingMembers.Split)));

        object?[] outputs = invoke([5.0, 2.0]);

        Assert.Equal(2, outputs.Length);
        Assert.Equal(7.0, outputs[0]);
        Assert.Equal(3.0, outputs[1]);
    }

    /// <summary>A member that produces nothing at all is refused rather than compiled into a node with no ports.</summary>
    [Fact]
    public void AVoidMemberWithNoOutParametersIsRefused()
    {
        MethodInfo method = Method(typeof(Uncompilable), nameof(Uncompilable.ProducesNothing));

        Assert.Throws<ArgumentException>(() => NodeInvoker.ForMethod(method));
    }

    /// <summary>
    /// The invoker is an expression-tree-compiled delegate, not a wrapper around
    /// <see cref="MethodBase.Invoke"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The discriminator is <c>Method.DeclaringType</c>. <c>Expression.Compile()</c> emits a
    /// <see cref="System.Reflection.Emit.DynamicMethod"/>, which belongs to no type and reports
    /// <see langword="null"/>. Every delegate written in C# source — including the one-line
    /// <c>arguments =&gt; method.Invoke(null, arguments)</c> this rule exists to forbid — is declared
    /// on the type that contains it. The test builds that exact forbidden delegate and asserts the
    /// discriminator separates the two, so it cannot pass by accident.
    /// </para>
    /// <para>
    /// <b>This was a timing test first, and timing turned out to be the wrong instrument.</b>
    /// ADR-0012 records the reflection path as 50 to 100 times slower, which was true when it was
    /// written. Measured here on .NET 10 over 200,000 calls to a two-argument static method,
    /// <c>MethodInfo.Invoke</c> is roughly <b>twice</b> as slow, not fifty times: the runtime now
    /// emits an invoke stub of its own. A ratio that small cannot be asserted without the test going
    /// red on a busy machine, so the guard is structural. The performance argument for compiling is
    /// still sound — 2x on the hottest path in the engine is not nothing, and the gap widens with
    /// argument count and with value-type boxing — but the number in the ADR no longer describes
    /// this runtime.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheInvokerIsACompiledDelegateAndNotAReflectionWrapper()
    {
        MethodInfo method = Method(typeof(LacingMembers), nameof(LacingMembers.Add));

        NodeInvocation compiled = NodeInvoker.ForMethod(method);
        NodeInvocation forbidden = arguments => [method.Invoke(null, arguments)];

        Assert.NotNull(forbidden.Method.DeclaringType);
        Assert.Null(compiled.Method.DeclaringType);

        // Both must still be correct, so that the discriminator is the only thing separating them.
        Assert.Equal(7.0, compiled([3.0, 4.0])[0]);
        Assert.Equal(7.0, forbidden([3.0, 4.0])[0]);
    }

    /// <summary>An open generic method cannot be compiled and says so rather than failing later.</summary>
    [Fact]
    public void AnOpenGenericMethodIsRefused()
    {
        MethodInfo method = typeof(Uncompilable).GetMethod(nameof(Uncompilable.Identity), BindingFlags.Public | BindingFlags.Static)!;

        Assert.Throws<ArgumentException>(() => NodeInvoker.ForMethod(method));
    }

    /// <summary>Members the invoker must refuse. Kept off the test class so xunit does not read them as tests.</summary>
    public static class Uncompilable
    {
        public static T Identity<T>(T value) => value;

        public static void ProducesNothing(int value) => _ = value;
    }

    private static MethodInfo Method(Type type, string name) =>
        type.GetMethod(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
        ?? throw new InvalidOperationException($"No method '{name}' on {type.Name}.");
}
