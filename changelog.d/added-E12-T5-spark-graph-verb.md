`spark graph GRAPH.spark` describes a graph file without opening it: the format version, the node
and wire counts, every definition it names marked present or missing against this build's library,
and the packages it records against the folder beside it.

Because it binds nothing, it is the one verb that still works on a graph this build cannot open —
which is when you want it. It exits 1 if anything the file names is missing here.
