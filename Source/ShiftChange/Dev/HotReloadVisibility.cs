#if DEBUG
// Hot-reload visibility grants — Debug builds only.
//
// ECR loads each reloaded image as a NEW assembly named ShiftChange1,
// ShiftChange2, … (AsmWriter: $"{assembly.Name}{version}") and redirects our
// methods into it, so every member a swapped body touches is a CROSS-ASSEMBLY
// access. ECR stamps IgnoresAccessChecksTo onto the swapped image for exactly
// this, but Unity's Mono does not implement that attribute (convicted
// 2026-08-08: attribute present, FieldAccessException anyway). The mechanism
// Mono has always honoured is InternalsVisibleTo — which is why this mod's
// default member visibility is `internal`, not `private`, and why these
// grants enumerate the numbered twins.
//
// The list length is the session's reload budget: reload number 65 starts
// throwing FieldAccessException again, and the fix is a game restart.

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("ShiftChange0")]
[assembly: InternalsVisibleTo("ShiftChange1")]
[assembly: InternalsVisibleTo("ShiftChange2")]
[assembly: InternalsVisibleTo("ShiftChange3")]
[assembly: InternalsVisibleTo("ShiftChange4")]
[assembly: InternalsVisibleTo("ShiftChange5")]
[assembly: InternalsVisibleTo("ShiftChange6")]
[assembly: InternalsVisibleTo("ShiftChange7")]
[assembly: InternalsVisibleTo("ShiftChange8")]
[assembly: InternalsVisibleTo("ShiftChange9")]
[assembly: InternalsVisibleTo("ShiftChange10")]
[assembly: InternalsVisibleTo("ShiftChange11")]
[assembly: InternalsVisibleTo("ShiftChange12")]
[assembly: InternalsVisibleTo("ShiftChange13")]
[assembly: InternalsVisibleTo("ShiftChange14")]
[assembly: InternalsVisibleTo("ShiftChange15")]
[assembly: InternalsVisibleTo("ShiftChange16")]
[assembly: InternalsVisibleTo("ShiftChange17")]
[assembly: InternalsVisibleTo("ShiftChange18")]
[assembly: InternalsVisibleTo("ShiftChange19")]
[assembly: InternalsVisibleTo("ShiftChange20")]
[assembly: InternalsVisibleTo("ShiftChange21")]
[assembly: InternalsVisibleTo("ShiftChange22")]
[assembly: InternalsVisibleTo("ShiftChange23")]
[assembly: InternalsVisibleTo("ShiftChange24")]
[assembly: InternalsVisibleTo("ShiftChange25")]
[assembly: InternalsVisibleTo("ShiftChange26")]
[assembly: InternalsVisibleTo("ShiftChange27")]
[assembly: InternalsVisibleTo("ShiftChange28")]
[assembly: InternalsVisibleTo("ShiftChange29")]
[assembly: InternalsVisibleTo("ShiftChange30")]
[assembly: InternalsVisibleTo("ShiftChange31")]
[assembly: InternalsVisibleTo("ShiftChange32")]
[assembly: InternalsVisibleTo("ShiftChange33")]
[assembly: InternalsVisibleTo("ShiftChange34")]
[assembly: InternalsVisibleTo("ShiftChange35")]
[assembly: InternalsVisibleTo("ShiftChange36")]
[assembly: InternalsVisibleTo("ShiftChange37")]
[assembly: InternalsVisibleTo("ShiftChange38")]
[assembly: InternalsVisibleTo("ShiftChange39")]
[assembly: InternalsVisibleTo("ShiftChange40")]
[assembly: InternalsVisibleTo("ShiftChange41")]
[assembly: InternalsVisibleTo("ShiftChange42")]
[assembly: InternalsVisibleTo("ShiftChange43")]
[assembly: InternalsVisibleTo("ShiftChange44")]
[assembly: InternalsVisibleTo("ShiftChange45")]
[assembly: InternalsVisibleTo("ShiftChange46")]
[assembly: InternalsVisibleTo("ShiftChange47")]
[assembly: InternalsVisibleTo("ShiftChange48")]
[assembly: InternalsVisibleTo("ShiftChange49")]
[assembly: InternalsVisibleTo("ShiftChange50")]
[assembly: InternalsVisibleTo("ShiftChange51")]
[assembly: InternalsVisibleTo("ShiftChange52")]
[assembly: InternalsVisibleTo("ShiftChange53")]
[assembly: InternalsVisibleTo("ShiftChange54")]
[assembly: InternalsVisibleTo("ShiftChange55")]
[assembly: InternalsVisibleTo("ShiftChange56")]
[assembly: InternalsVisibleTo("ShiftChange57")]
[assembly: InternalsVisibleTo("ShiftChange58")]
[assembly: InternalsVisibleTo("ShiftChange59")]
[assembly: InternalsVisibleTo("ShiftChange60")]
[assembly: InternalsVisibleTo("ShiftChange61")]
[assembly: InternalsVisibleTo("ShiftChange62")]
[assembly: InternalsVisibleTo("ShiftChange63")]
[assembly: InternalsVisibleTo("ShiftChange64")]
#endif
