using System;

namespace Spark.Geometry.Occt;

/// <summary>
/// A <see cref="Brep"/> that lives inside OpenCascade until something asks it not to.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the whole of ADR-0021 in one class.</b> After an operation the provider's shape is
/// canonical and Spark's arrays do not exist yet; <see cref="Materialise"/> builds them the first
/// time a structural question is asked, and a chain of ten booleans therefore performs zero
/// imports and one read at the end. Doing it the other way — converting after every step — would
/// re-sew and re-tolerance the user's geometry ten times while they did nothing.
/// </para>
/// <para>
/// <b>The read is done once and remembered, and the handle is kept afterwards.</b> Keeping it is
/// the point: the next boolean in the chain needs the provider's shape, not a re-import of the
/// arrays that were read out of it, and re-importing is exactly the drift the decision avoids.
/// </para>
/// </remarks>
internal sealed class OcctResidency : BrepResidency
{
    private readonly OcctShape _shape;
    private readonly System.Threading.Lock _gate = new();

    private BrepData? _materialised;

    /// <summary>Wraps a shape the provider has just produced.</summary>
    public OcctResidency(OcctShape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);

        _shape = shape;
    }

    /// <summary>The provider's shape, for the next operation in a chain.</summary>
    public OcctShape Shape => _shape;

    /// <inheritdoc/>
    public override long NativeBytes => _shape.Bytes;

    /// <inheritdoc/>
    public override BrepData Materialise()
    {
        lock (_gate)
        {
            if (_materialised is { } already)
            {
                return already;
            }

            if (_shape.IsInvalid || _shape.IsClosed)
            {
                throw new ObjectDisposedException(
                    nameof(OcctResidency), "The shape was released before it was read.");
            }

            int status = NativeMethods.spark_occt_read(_shape.Pointer, 0.0, out IntPtr raw);

            if (status != NativeMethods.Ok)
            {
                throw new InvalidOperationException(NativeErrors.Describe(status, "Reading the shape"));
            }

            using OcctModel model = OcctModel.Own(raw);
            BrepData data = ModelReader.Read(model.Pointer);
            _materialised = data;

            return data;
        }
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        _shape.Dispose();
    }
}
