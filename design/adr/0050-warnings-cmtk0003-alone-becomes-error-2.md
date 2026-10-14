# 0050. Of the Warnings, CMTK0003 alone becomes an Error in 2.0.0

Date: 2026-10-13
Status: accepted

Context: 2026-10-13, phase 13.

Decision 34
lets a major raise a Warning; the bar for raising one is that every report can be answered in the
code by a form that says what is meant, never by a suppression alone, and that its reach is
complete, since an Error cannot learn within the major. CMTK0003 meets both: `_ =`, a cast or a
loop variable named `_` says a drop is meant, and its reach is a return type rather than a list
of methods, complete once its 1.x gaps were closed (`Wait`, `Task.WaitAll`, a loop that never
reads its results, a lazy sequence materialised). The others stay. CMTK0005's array filled in a
loop and CMTK0006's member set in a second step (`OnInitialized`, `InitializeAsync`, a private
field `required` cannot reach) have only a suppression to say so. CMTK0008's shared identities
are answered by a cast an IDE reports as redundant, and its four known gaps would be frozen for
2.x. CMTK0009's reach is a list of framework methods, which the `net11.0` target of a 2.x minor
may lengthen, and a sequence checked for emptiness first is correct. CMTK0007 reports test
theories. The 1.5.1 baseline's verdict, over all seven packages: under package validation's
default rules one difference, CP0021 on `FaultProblem<TFault>` ([decision 48](0048-intended-break-suppressed-alone-baseline-stays.md)); with the
attribute and parameter-name rules on as well, 29 compiler-emitted attributes (state machines
renumbered by members added before them, nullable-context bytes) and no renamed parameter. The
analyzer's severity and the query overloads' binding are behaviour no compatibility check reads;
the changelog states each.
