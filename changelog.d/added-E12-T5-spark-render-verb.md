`spark render --open GRAPH.spark --out FILE.png` evaluates a graph with no window and writes a
picture of it.

It draws through the software rasteriser rather than the GPU, so the same graph produces the same
bytes on a machine with no display and no driver. It exits 2 if the graph ran but produced nothing
to draw, because an empty image written silently is the failure a visual check exists to catch.
