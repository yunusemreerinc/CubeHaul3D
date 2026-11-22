using System.Runtime.CompilerServices;

// Board's depth/road bookkeeping is internal; the tests compare it with a fresh computation (invariant I-09).
[assembly: InternalsVisibleTo("CubeHaul.Tests.EditMode")]
[assembly: InternalsVisibleTo("CubeHaul.Tests")]
