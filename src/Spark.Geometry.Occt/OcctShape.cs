using System;
using System.Runtime.InteropServices;

namespace Spark.Geometry.Occt;

/// <summary>
/// A live OpenCascade shape, owned by this process and released exactly once.
/// </summary>
/// <remarks>
/// <b>A <see cref="SafeHandle"/> rather than a raw <see cref="IntPtr"/>, because a resident BRep
/// outlives the call that made it.</b> ADR-0021 makes the provider's representation canonical,
/// which means a <see cref="Brep"/> can sit in an evaluation cache for an hour holding native
/// memory. The handle's own finalizer is the backstop for the case nobody disposes it, and its
/// reference counting is what stops a shape being freed while a tessellation is reading it.
/// </remarks>
internal sealed class OcctShape : SafeHandle
{
    private OcctShape()
        : base(IntPtr.Zero, ownsHandle: true)
    {
    }

    /// <inheritdoc/>
    public override bool IsInvalid => handle == IntPtr.Zero;

    /// <summary>Takes ownership of a handle the provider just returned.</summary>
    public static OcctShape Own(IntPtr value)
    {
        OcctShape shape = new();
        shape.SetHandle(value);

        return shape;
    }

    /// <summary>Roughly how much native memory the shape occupies.</summary>
    public long Bytes => IsInvalid ? 0L : NativeMethods.spark_occt_shape_bytes(handle);

    /// <summary>The raw pointer, for the duration of a call that holds a reference.</summary>
    public IntPtr Pointer => handle;

    /// <inheritdoc/>
    protected override bool ReleaseHandle()
    {
        NativeMethods.spark_occt_shape_release(handle);

        return true;
    }
}

/// <summary>
/// A triangulation the provider produced, released once it has been copied out.
/// </summary>
internal sealed class OcctMesh : SafeHandle
{
    private OcctMesh()
        : base(IntPtr.Zero, ownsHandle: true)
    {
    }

    /// <inheritdoc/>
    public override bool IsInvalid => handle == IntPtr.Zero;

    /// <summary>The raw pointer.</summary>
    public IntPtr Pointer => handle;

    /// <summary>Takes ownership of a handle the provider just returned.</summary>
    public static OcctMesh Own(IntPtr value)
    {
        OcctMesh mesh = new();
        mesh.SetHandle(value);

        return mesh;
    }

    /// <inheritdoc/>
    protected override bool ReleaseHandle()
    {
        NativeMethods.spark_occt_mesh_release(handle);

        return true;
    }
}

/// <summary>
/// A model the provider read out, released once it has been decoded.
/// </summary>
internal sealed class OcctModel : SafeHandle
{
    private OcctModel()
        : base(IntPtr.Zero, ownsHandle: true)
    {
    }

    /// <inheritdoc/>
    public override bool IsInvalid => handle == IntPtr.Zero;

    /// <summary>The raw pointer.</summary>
    public IntPtr Pointer => handle;

    /// <summary>Takes ownership of a handle the provider just returned.</summary>
    public static OcctModel Own(IntPtr value)
    {
        OcctModel model = new();
        model.SetHandle(value);

        return model;
    }

    /// <inheritdoc/>
    protected override bool ReleaseHandle()
    {
        NativeMethods.spark_occt_model_release(handle);

        return true;
    }
}
