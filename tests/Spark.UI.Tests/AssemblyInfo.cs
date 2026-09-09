using Xunit.Sdk;
using Xunit.v3;

// `E11-T27`: ONE TEST AT A TIME IN THIS ASSEMBLY, AND THE SIXTEEN SECONDS ARE WORTH IT.
//
// Avalonia's `Dispatcher.UIThread` is process-global state that the one headless session owns.
// `E11-T26` put a semaphore inside `HeadlessSession.Run`, which serialises every test that shows a
// window — and that fixed the common case, evidenced 2 failures in 16 runs before against 0 in 26
// after. What it cannot serialise is a test that touches Avalonia **without** going through it: a
// pure view-model test builds a `MainWindowViewModel`, which posts to that same global dispatcher
// and lazily builds a `DispatcherTimer`. Racing a session test is then a race by construction, and
// no amount of care inside `HeadlessSession.Run` can see it.
//
// `E11-T27` accumulated ten victims across five classes over three days — a different test each
// time, which is the tell that it is one defect and not ten. At that rate the suite failed roughly
// one full run in three.
//
// THE ALTERNATIVE WAS ONE XUNIT COLLECTION OVER EVERY AVALONIA-TOUCHING CLASS, and it was rejected
// for being a rule somebody has to keep. A collection has to gain each new class as it is written,
// forgetting is silent, and forgetting is exactly how this row grew ten times while people were
// doing something else. This attribute cannot be forgotten.
//
// THE COST IS MEASURED, NOT ESTIMATED: see the task row. It buys a suite whose green means green,
// which matters more since `E13-T19` turned CI off and left the local run as the only evidence
// there is.
// xunit v3 spells it this way; `CollectionBehavior(DisableTestParallelization: true)` is obsolete
// and would fail the build under `-warnaserror`.
[assembly: Parallelization(Mode = ParallelMode.None)]
