A node imported from an `async` member now produces its awaited result. It produced the task object
itself, on a port typed `Task<T>`, which nothing downstream could use.

`ValueTask` is handled the same way, and an asynchronous member that returns no value is excluded
with a reason rather than becoming a node that produces nothing.
