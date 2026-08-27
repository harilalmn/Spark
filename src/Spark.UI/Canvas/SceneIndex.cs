using System;
using System.Collections.Generic;

namespace Spark.UI.Canvas;

/// <summary>
/// Culling and hit-testing over the whole node canvas: structure-of-arrays world bounds, a coarse
/// uniform grid packed CSR-style, and a visibility bitset whose set bits come out in draw order.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ported from <c>DoodleSharp/Rendering/SceneIndex.cs</c>.</b> The reasoning below is that
/// file's and is reproduced because it is the reason this class exists rather than a quadtree.
/// What changed in the port is that the payload is an opaque <c>int</c> item id instead of a WPF
/// drawable, and bounds are supplied by the caller instead of being read back off a shape — which
/// is what makes this the only piece of the prior art's UI layer that transfers at all.
/// </para>
/// <para>
/// <b>Why a grid and not a tree.</b> A quadtree degenerates in exactly the case a real graph is
/// made of — a dense cluster bottoms out at the depth limit and every leaf becomes a linear scan,
/// while items straddling a boundary are stored in up to four subtrees. A uniform grid inverts
/// that: a dense cluster is one long cell list, and a straight scan over a contiguous run of
/// indices is the case a modern CPU is best at.
/// </para>
/// <para>
/// <b>Why draw order comes for free.</b> The canvas draws back to front, so it needs the visible
/// set back in slot order, and sorting a few thousand indices per frame would cost more than the
/// culling saves. The slot index <i>is</i> the draw order, a query sets bits in a
/// <see cref="ulong"/> array, and <see cref="Visible"/> walks the set bits ascending by
/// construction: no sort, no allocation.
/// </para>
/// <para>
/// <b>An item is entered into every cell its box overlaps</b>, not just the cell holding a
/// corner. Binning by corner and widening the query by one cell looks cheaper and breaks down the
/// moment an item is larger than a cell — in DoodleSharp that turned a 45-shape frame into a
/// 9,844-shape scan. Only genuinely huge items, spanning more than
/// <see cref="MaxCellsPerItem"/> cells, go to the always-scanned oversize list.
/// </para>
/// </remarks>
public sealed class SceneIndex
{
    /// <summary>
    /// Rebuild once incremental additions exceed this share of the indexed set. Low enough that
    /// the always-scanned overflow never dominates a query, high enough that dragging out a few
    /// dozen nodes does not rebuild on every mouse-up.
    /// </summary>
    private const double OverflowRebuildFraction = 0.05;

    private const int OverflowRebuildFloor = 512;

    /// <summary>
    /// Target average occupancy per cell. Chosen so a cell scan stays within a cache line's worth
    /// of indices while the grid itself stays small enough to clear cheaply.
    /// </summary>
    private const int TargetItemsPerCell = 4;

    private const int MaxGridDimension = 2048;

    /// <summary>
    /// An item covering more cells than this is held out of the grid and tested on every query.
    /// Beyond roughly this many entries, duplicating an item across cells costs more — in build
    /// time, memory and cache misses — than testing it each frame.
    /// </summary>
    private const int MaxCellsPerItem = 32;

    private double[] _minX = [];
    private double[] _minY = [];
    private double[] _maxX = [];
    private double[] _maxY = [];
    private bool[] _live = [];
    private int _slotCount;

    private int[] _cellStart = [];
    private int[] _cellItems = [];
    private int _columns;
    private int _rows;
    private double _originX;
    private double _originY;
    private double _cellSize = 1;

    private readonly List<int> _oversize = [];
    private readonly List<int> _overflow = [];
    private readonly HashSet<int> _overflowSet = [];

    private ulong[] _visible = [];

    /// <summary>
    /// Per-slot "last query that touched this" marker. An item appears in every cell its box
    /// overlaps, so a query meets the same slot several times; stamping keeps
    /// <see cref="ConsideredCount"/> honest and lets a query emit each item once without a hash
    /// set. Comparing against a rising id avoids clearing the array per query.
    /// </summary>
    private int[] _stamp = [];
    private int _queryId;

    private int _visibleMinSlot = int.MaxValue;
    private int _visibleMaxSlot = int.MinValue;
    private int _visibleCount;
    private int _consideredCount;

    /// <summary>Slots currently indexed, including tombstoned ones.</summary>
    public int SlotCount => _slotCount;

    /// <summary>How many items the last <see cref="Query"/> marked visible.</summary>
    public int VisibleCount => _visibleCount;

    /// <summary>
    /// How many items the last <see cref="Query"/> had to test. The ratio against
    /// <see cref="VisibleCount"/> is the whole point of this class: when it equals the graph size,
    /// culling is doing nothing and something is wrong with the grid.
    /// </summary>
    public int ConsideredCount => _consideredCount;

    /// <summary>True when incremental changes have grown enough to be worth a rebuild.</summary>
    public bool NeedsRebuild =>
        _overflow.Count > Math.Max(OverflowRebuildFloor, _slotCount * OverflowRebuildFraction);

    /// <summary>Whether a slot holds a live item.</summary>
    /// <param name="slot">The slot to test.</param>
    /// <returns>False for an out-of-range or tombstoned slot.</returns>
    public bool IsLive(int slot) => (uint)slot < (uint)_slotCount && _live[slot];

    /// <summary>The cached bounds of a slot.</summary>
    /// <param name="slot">The slot to read.</param>
    /// <param name="minX">The left edge.</param>
    /// <param name="minY">The top edge.</param>
    /// <param name="maxX">The right edge.</param>
    /// <param name="maxY">The bottom edge.</param>
    /// <remarks>
    /// Bounds are read back from the index rather than recomputed. The index already read them
    /// once at build time, and level-of-detail decisions need the size of an item on every frame.
    /// </remarks>
    public void BoundsAt(int slot, out double minX, out double minY, out double maxX, out double maxY)
    {
        minX = _minX[slot];
        minY = _minY[slot];
        maxX = _maxX[slot];
        maxY = _maxY[slot];
    }

    /// <summary>The larger of a slot's cached width and height.</summary>
    /// <param name="slot">The slot to measure.</param>
    /// <returns>The extent, or zero for a dead slot.</returns>
    public double MaxExtentAt(int slot)
    {
        if (!IsLive(slot))
        {
            return 0;
        }

        double width = _maxX[slot] - _minX[slot];
        double height = _maxY[slot] - _minY[slot];
        return width > height ? width : height;
    }

    /// <summary>
    /// Indexes a whole set of items. O(n), and the only place bounds are read.
    /// </summary>
    /// <param name="bounds">
    /// One entry per item, in draw order — index 0 is drawn first and therefore sits at the
    /// bottom. Slot numbers are positions in this list and are what every other member speaks in.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="bounds"/> is null.</exception>
    public void Rebuild(IReadOnlyList<CanvasBounds> bounds)
    {
        ArgumentNullException.ThrowIfNull(bounds);

        int count = bounds.Count;
        EnsureSlotCapacity(count);
        _slotCount = count;

        _oversize.Clear();
        _overflow.Clear();
        _overflowSet.Clear();

        double worldMinX = double.MaxValue;
        double worldMinY = double.MaxValue;
        double worldMaxX = double.MinValue;
        double worldMaxY = double.MinValue;

        for (int i = 0; i < count; i++)
        {
            CanvasBounds box = bounds[i];
            _live[i] = true;
            _minX[i] = box.MinX;
            _minY[i] = box.MinY;
            _maxX[i] = box.MaxX;
            _maxY[i] = box.MaxY;

            if (box.MinX < worldMinX)
            {
                worldMinX = box.MinX;
            }

            if (box.MinY < worldMinY)
            {
                worldMinY = box.MinY;
            }

            if (box.MaxX > worldMaxX)
            {
                worldMaxX = box.MaxX;
            }

            if (box.MaxY > worldMaxY)
            {
                worldMaxY = box.MaxY;
            }
        }

        EnsureVisibleCapacity(count);
        _queryId = 0;
        Array.Clear(_stamp);
        ClearVisible();
        _visibleCount = 0;
        _consideredCount = 0;

        if (count == 0)
        {
            _columns = 0;
            _rows = 0;
            return;
        }

        ChooseGrid(worldMinX, worldMinY, worldMaxX, worldMaxY, count);
        BuildCells(count);
    }

    /// <summary>
    /// Appends an item. It goes on the always-scanned overflow list rather than into the grid,
    /// which keeps the operation O(1); once the overflow stops being negligible relative to the
    /// indexed set, <see cref="NeedsRebuild"/> goes true and the caller rebuilds.
    /// </summary>
    /// <param name="bounds">The new item's bounds.</param>
    /// <returns>The slot the item was given, which is also its draw order.</returns>
    public int Add(CanvasBounds bounds)
    {
        EnsureSlotCapacity(_slotCount + 1);
        EnsureVisibleCapacity(_slotCount + 1);

        int slot = _slotCount++;
        _live[slot] = true;
        _minX[slot] = bounds.MinX;
        _minY[slot] = bounds.MinY;
        _maxX[slot] = bounds.MaxX;
        _maxY[slot] = bounds.MaxY;
        _overflow.Add(slot);
        _overflowSet.Add(slot);
        return slot;
    }

    /// <summary>
    /// Updates one item's bounds in place, which is what a node drag does on every pointer move.
    /// </summary>
    /// <param name="slot">The slot to move.</param>
    /// <param name="bounds">The new bounds.</param>
    /// <remarks>
    /// The item moves onto the always-scanned overflow list rather than being rebinned, because a
    /// drag touches one item per frame and a rebin would touch up to
    /// <see cref="MaxCellsPerItem"/> cells twice. <see cref="NeedsRebuild"/> catches up later.
    /// </remarks>
    public void Update(int slot, CanvasBounds bounds)
    {
        if (!IsLive(slot))
        {
            return;
        }

        _minX[slot] = bounds.MinX;
        _minY[slot] = bounds.MinY;
        _maxX[slot] = bounds.MaxX;
        _maxY[slot] = bounds.MaxY;

        if (_overflowSet.Add(slot))
        {
            _overflow.Add(slot);
        }
    }

    /// <summary>Tombstones a slot. The slot number is never reused until the next rebuild.</summary>
    /// <param name="slot">The slot to remove.</param>
    /// <returns>True when the slot was live.</returns>
    public bool Remove(int slot)
    {
        if (!IsLive(slot))
        {
            return false;
        }

        _live[slot] = false;
        return true;
    }

    /// <summary>Empties the index.</summary>
    public void Clear()
    {
        _slotCount = 0;
        _columns = 0;
        _rows = 0;
        _oversize.Clear();
        _overflow.Clear();
        _overflowSet.Clear();
        ClearVisible();
        _visibleCount = 0;
        _consideredCount = 0;
    }

    /// <summary>
    /// Marks every item overlapping a world rectangle. Allocation-free; the results are read back
    /// through <see cref="Visible"/> in draw order or <see cref="VisibleTopDown"/> for hit-testing.
    /// </summary>
    /// <param name="minX">The left edge of the query rectangle.</param>
    /// <param name="minY">The top edge.</param>
    /// <param name="maxX">The right edge.</param>
    /// <param name="maxY">The bottom edge.</param>
    public void Query(double minX, double minY, double maxX, double maxY)
    {
        NextQuery();
        ClearVisible();
        _visibleCount = 0;
        _consideredCount = 0;

        foreach (int slot in _oversize)
        {
            TestAndMark(slot, minX, minY, maxX, maxY);
        }

        foreach (int slot in _overflow)
        {
            TestAndMark(slot, minX, minY, maxX, maxY);
        }

        if (_columns == 0 || _rows == 0)
        {
            return;
        }

        GetCellRange(minX, minY, maxX, maxY, out int c0, out int r0, out int c1, out int r1);

        for (int r = r0; r <= r1; r++)
        {
            int rowBase = r * _columns;
            for (int c = c0; c <= c1; c++)
            {
                int cell = rowBase + c;
                int end = _cellStart[cell + 1];
                for (int k = _cellStart[cell]; k < end; k++)
                {
                    TestAndMark(_cellItems[k], minX, minY, maxX, maxY);
                }
            }
        }
    }

    /// <summary>
    /// The topmost live slot whose bounds contain a point, or −1.
    /// </summary>
    /// <param name="x">The world x coordinate.</param>
    /// <param name="y">The world y coordinate.</param>
    /// <returns>The slot, or −1 when nothing is under the point.</returns>
    /// <remarks>
    /// This runs a point query and then walks the visible set from the top down, so it answers in
    /// the order a user perceives: the thing drawn last is the thing you clicked.
    /// </remarks>
    public int HitTest(double x, double y)
    {
        Query(x, y, x, y);

        foreach (int slot in VisibleTopDown)
        {
            return slot;
        }

        return -1;
    }

    /// <summary>The visible slots, ascending, which is draw order.</summary>
    public VisibleEnumerator Visible => new(this, ascending: true);

    /// <summary>The visible slots, descending — topmost first, which is what hit-testing wants.</summary>
    public VisibleEnumerator VisibleTopDown => new(this, ascending: false);

    private void ChooseGrid(double minX, double minY, double maxX, double maxY, int count)
    {
        double width = Math.Max(maxX - minX, 1e-9);
        double height = Math.Max(maxY - minY, 1e-9);

        // Aim for TargetItemsPerCell on average, then clamp the grid so a pathological aspect
        // ratio or a huge item count cannot produce a cell array bigger than the scene itself.
        double targetCells = Math.Max(1.0, count / (double)TargetItemsPerCell);
        double cell = Math.Sqrt(width * height / targetCells);
        if (!double.IsFinite(cell) || cell <= 0)
        {
            cell = Math.Max(width, height);
        }

        _columns = Math.Clamp((int)Math.Ceiling(width / cell) + 1, 1, MaxGridDimension);
        _rows = Math.Clamp((int)Math.Ceiling(height / cell) + 1, 1, MaxGridDimension);

        // Re-derive the cell size from the clamped dimensions so the grid still spans the world.
        _cellSize = Math.Max(width / _columns, height / _rows);
        if (!double.IsFinite(_cellSize) || _cellSize <= 0)
        {
            _cellSize = 1;
        }

        _originX = minX;
        _originY = minY;
    }

    /// <summary>
    /// Counting sort of slots into cells. Two passes over the items and no per-cell list objects;
    /// the whole grid is two <c>int</c> arrays.
    /// </summary>
    /// <param name="count">The number of slots to bin.</param>
    private void BuildCells(int count)
    {
        int cellCount = _columns * _rows;
        if (_cellStart.Length < cellCount + 1)
        {
            _cellStart = new int[cellCount + 1];
        }
        else
        {
            Array.Clear(_cellStart, 0, cellCount + 1);
        }

        int entries = 0;
        for (int i = 0; i < count; i++)
        {
            if (!_live[i])
            {
                continue;
            }

            CellSpan(i, out int c0, out int r0, out int c1, out int r1);
            long span = (long)(c1 - c0 + 1) * (r1 - r0 + 1);

            if (span > MaxCellsPerItem)
            {
                _oversize.Add(i);
                continue;
            }

            for (int r = r0; r <= r1; r++)
            {
                for (int c = c0; c <= c1; c++)
                {
                    _cellStart[(r * _columns) + c + 1]++;
                }
            }

            entries += (int)span;
        }

        for (int c = 0; c < cellCount; c++)
        {
            _cellStart[c + 1] += _cellStart[c];
        }

        if (_cellItems.Length < entries)
        {
            _cellItems = new int[Math.Max(entries, 16)];
        }

        int[] cursor = new int[cellCount];
        Array.Copy(_cellStart, cursor, cellCount);

        for (int i = 0; i < count; i++)
        {
            if (!_live[i])
            {
                continue;
            }

            CellSpan(i, out int c0, out int r0, out int c1, out int r1);
            if ((long)(c1 - c0 + 1) * (r1 - r0 + 1) > MaxCellsPerItem)
            {
                continue;
            }

            for (int r = r0; r <= r1; r++)
            {
                for (int c = c0; c <= c1; c++)
                {
                    _cellItems[cursor[(r * _columns) + c]++] = i;
                }
            }
        }
    }

    /// <summary>The inclusive cell rectangle an item's bounding box covers.</summary>
    private void CellSpan(int slot, out int c0, out int r0, out int c1, out int r1)
    {
        c0 = Math.Clamp((int)((_minX[slot] - _originX) / _cellSize), 0, _columns - 1);
        c1 = Math.Clamp((int)((_maxX[slot] - _originX) / _cellSize), 0, _columns - 1);
        r0 = Math.Clamp((int)((_minY[slot] - _originY) / _cellSize), 0, _rows - 1);
        r1 = Math.Clamp((int)((_maxY[slot] - _originY) / _cellSize), 0, _rows - 1);
    }

    private void GetCellRange(double minX, double minY, double maxX, double maxY,
        out int c0, out int r0, out int c1, out int r1)
    {
        c0 = Math.Clamp((int)Math.Floor((minX - _originX) / _cellSize), 0, _columns - 1);
        c1 = Math.Clamp((int)Math.Floor((maxX - _originX) / _cellSize), 0, _columns - 1);
        r0 = Math.Clamp((int)Math.Floor((minY - _originY) / _cellSize), 0, _rows - 1);
        r1 = Math.Clamp((int)Math.Floor((maxY - _originY) / _cellSize), 0, _rows - 1);
    }

    /// <summary>
    /// Advances the query stamp. Wrapping is handled by clearing rather than by letting the
    /// comparison alias — at one query per frame that is once every 2.6 million frames, but a
    /// stale stamp would drop items from a frame, and that is not a bug worth leaving to chance.
    /// </summary>
    private void NextQuery()
    {
        if (++_queryId == int.MaxValue)
        {
            Array.Clear(_stamp);
            _queryId = 1;
        }
    }

    private void TestAndMark(int slot, double minX, double minY, double maxX, double maxY)
    {
        if (!_live[slot] || _stamp[slot] == _queryId)
        {
            return;
        }

        _stamp[slot] = _queryId;
        _consideredCount++;

        if (_minX[slot] > maxX || _maxX[slot] < minX || _minY[slot] > maxY || _maxY[slot] < minY)
        {
            return;
        }

        Mark(slot);
    }

    private void Mark(int slot)
    {
        int word = slot >> 6;
        ulong bit = 1UL << (slot & 63);
        if ((_visible[word] & bit) != 0)
        {
            return;
        }

        _visible[word] |= bit;
        _visibleCount++;

        if (slot < _visibleMinSlot)
        {
            _visibleMinSlot = slot;
        }

        if (slot > _visibleMaxSlot)
        {
            _visibleMaxSlot = slot;
        }
    }

    private void ClearVisible()
    {
        if (_visibleMaxSlot >= _visibleMinSlot && _visible.Length > 0)
        {
            int from = _visibleMinSlot >> 6;
            int to = Math.Min(_visibleMaxSlot >> 6, _visible.Length - 1);
            Array.Clear(_visible, from, to - from + 1);
        }

        _visibleMinSlot = int.MaxValue;
        _visibleMaxSlot = int.MinValue;
    }

    private void EnsureSlotCapacity(int needed)
    {
        if (_minX.Length >= needed)
        {
            return;
        }

        int size = Math.Max(needed, Math.Max(16, _minX.Length * 2));
        Array.Resize(ref _minX, size);
        Array.Resize(ref _minY, size);
        Array.Resize(ref _maxX, size);
        Array.Resize(ref _maxY, size);
        Array.Resize(ref _live, size);
    }

    private void EnsureVisibleCapacity(int slots)
    {
        int words = (slots + 63) >> 6;
        if (_visible.Length < words)
        {
            Array.Resize(ref _visible, Math.Max(words, 4));
        }

        if (_stamp.Length < slots)
        {
            Array.Resize(ref _stamp, Math.Max(slots, 16));
        }
    }

    /// <summary>
    /// Walks the visible slots. A struct enumerator with its own <c>GetEnumerator</c>, so
    /// <c>foreach</c> over it allocates nothing — which matters because it runs twice per frame at
    /// two thousand nodes.
    /// </summary>
    public struct VisibleEnumerator
    {
        private readonly SceneIndex _owner;
        private readonly bool _ascending;
        private int _slot;
        private bool _started;

        internal VisibleEnumerator(SceneIndex owner, bool ascending)
        {
            _owner = owner;
            _ascending = ascending;
            _slot = -1;
            _started = false;
        }

        /// <summary>The slot the enumerator is currently on.</summary>
        public readonly int Current => _slot;

        /// <summary>Returns this enumerator, so it can be used directly in <c>foreach</c>.</summary>
        /// <returns>A copy of this enumerator.</returns>
        public readonly VisibleEnumerator GetEnumerator() => this;

        /// <summary>Advances to the next visible slot.</summary>
        /// <returns>False when the visible set is exhausted.</returns>
        public bool MoveNext()
        {
            SceneIndex owner = _owner;
            if (owner._visibleCount == 0 || owner._visibleMaxSlot < owner._visibleMinSlot)
            {
                return false;
            }

            if (!_started)
            {
                _started = true;
                _slot = _ascending ? owner._visibleMinSlot - 1 : owner._visibleMaxSlot + 1;
            }

            if (_ascending)
            {
                for (int s = _slot + 1; s <= owner._visibleMaxSlot; s++)
                {
                    if ((owner._visible[s >> 6] & (1UL << (s & 63))) != 0)
                    {
                        _slot = s;
                        return true;
                    }
                }
            }
            else
            {
                for (int s = _slot - 1; s >= owner._visibleMinSlot; s--)
                {
                    if ((owner._visible[s >> 6] & (1UL << (s & 63))) != 0)
                    {
                        _slot = s;
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
