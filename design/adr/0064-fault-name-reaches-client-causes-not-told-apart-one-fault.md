# 0064. A fault's name reaches the client; causes it must not tell apart are one fault

Date: 2026-10-13
Status: accepted

Context: 2026-10-13, the pre-tag review of 2.0.0; [decision 38](0038-result-becomes-response-explicit-tohttpresult-faults.md),
[decision 49](0049-fault-responses-client-can-act.md).

Every problem `ToHttpResult()` writes carries `fault`, its member's name ([decision 38](0038-result-becomes-response-explicit-tohttpresult-faults.md),
since 1.5), for a member without a description too, and the document describes it
([decision 49](0049-fault-responses-client-can-act.md)). The review named the case that matters: a
sign-in that answers 401 both for an unknown user and for a wrong password still says which, and so
which accounts exist.

For 2.0.0 the name stays in every fault response, and the package READMEs and `docs/` say it is part
of the response's contract. Where two causes must not be told apart, the boundary maps them to one
member of its own fault type before `ToHttpResult()` (`MapError(_ => LoginFault.InvalidCredentials)`);
the AspNetCore README shows it as compiled code, and `SignInSampleTests` holds that the two then
answer alike. It needs no API, and it puts the choice where the meaning is: the boundary's fault type
says what the client may know.

An opt-in that withholds the name for chosen members or a whole fault type would be additive, and
joins 2.1. Rejected for 2.0.0: withholding it by default, since `fault` is what a client matches on
and 1.5's responses carried it; and withholding it for a member without a description, since a
description is for people and says nothing about whether the name may be known.
