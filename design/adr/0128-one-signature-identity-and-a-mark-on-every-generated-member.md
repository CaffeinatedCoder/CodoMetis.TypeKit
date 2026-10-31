# 0128. One signature identity for every hand-written-member check, the generated members read from the aspects, and a mark on each

Date: 2026-10-31
Status: accepted

Context: [#239](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/issues/239), under the 2.4.0 epic
[#177](https://github.com/CaffeinatedCoder/CodoMetis.TypeKit/issues/177); refines
[decision 28](0028-seams-declared-everything-else-refused.md) and
[decision 121](0121-a-generated-member-is-refused-wherever-the-value-object-reaches-it.md). Seven 2.3.1 bugs (#191,
#195, #196, #197, #205, #226, #230) were one question answered differently in several places: *is this member the
user wrote the one a generator introduces?* `ValueObjectDeclaration.MethodsWithSignature` compared parameter types
with `Equals`, `ClosedSetTypes.SameParameters` and the base-record seam walk after `ToNonNullable()`, which also turns
`int?` into `int`; `MembersNamed` took every member of a name on the value object and on its bases, while the closed
set's base walk took fields, properties and events only. The generated members' names were lists kept by hand
(`ValueObjectDeclaration.GeneratedMemberNames`, `ClosedSetTypes.GeneratedNames`), and AGENTS.md warned that a new
generated member left off them is kept silently.

## What was measured

On `refactor/generated-member-identity`, from `release/2.4.0` at `af3f95e`, 2026-10-31, each declaration in a
consumer of the generators at `af3f95e` and of this change, the second with warnings as errors:

- **Another kind of member of a generated member's name.** A property or field named `Parse`, `TryFormat`, `TryFrom` or
  `CompareTo`, on the value object or on a base record, failed the aspect: LAMA0506 ("there is already a property of
  the same name declared in the type or in a base type"), naming nothing the user wrote. A closed set's own method
  `Name()` failed inside the generated code (LAMA0611, CS0102), and a base class's nested type of the generated
  converter's name failed the aspect (LAMA0531). A base class's method `All()`, beside the generated property `All`,
  built: the property hides it.
- **`Nullable<T>` read as `T`.** A base record's `TryFrom(int?)` over an `int` and a closed set's
  `Parse(string, bool?)` were CMTK1011, while the value object's own `TryFrom(int?)` built, the generated
  `TryFrom(int)` beside it. C# overloads the two: `int?` is `Nullable<int>`, another type.
- **A base record's method of a generated property's or field's name** (`MinValue()`, `Value()`, `_value()`), and an
  overload of an entry point of another signature (`__FromJson(string)` over an `int`), were CMTK1011. Let through,
  each builds with warnings as errors, and `MinValue`, `Value`, JSON, `TryFrom` and `Parse` answer through the
  generated members (a run of the built consumer printed each as expected).
- **Metalama applies no attribute to an override it writes over an implicit member.** The value object's `ToString()`,
  and `Equals(TSelf)` and `GetHashCode()` over a base record, override what the record synthesizes in its own type;
  Metalama writes them over that declaration, applies none of the build delegate's attributes there (the
  `[CompilerGenerated]` the aspects asked for since 1.0.0 was never on them either), and refuses
  `IntroduceAttribute` on it ("implicitly declared and not in an allowed category").

## Decision

**One identity, C#'s** (`MemberIdentity`). A hand-written member takes a generated one's place when it is visible from
the value object (a base's private member is not: #230) and either

- is a method with the generated method's signature: its name, its arity, and each parameter's type without its
  nullable annotation and whether it is by reference (`ref`, `in` and `out` are one, by value another: CS0663). The
  return type and whether it is static are not part of it. `Nullable<T>` stays a type of its own; or
- has its name and another kind: a property, field, event or nested type beside any generated member, in the type or
  a base (LAMA0506, LAMA0531); a method beside a generated property, field or nested type in the type only (CS0102),
  since a base's method is hidden by it.

The return type and static-ness stay what each *seam* is: an instance `TryFrom` is no factory, and a static
`ToString()` no seam. A seam (`TryFrom`, `FromKnownGood`, `Revalidate`, `CompareTo(TSelf)`, `ToString()`) of the
generated signature is kept, so only another kind of member of its name is refused; `CompareTo(object)` stays
CMTK1008's. The equality checks are not overload questions and keep their own rule, an `Equals` of one of the value
object's own types in either nullability (decision 75, #196), through the identity's arity and ref-kind test.

**The generated members are read from what the aspects introduce** (`GeneratedMembers`). Each named member the
generators introduce into a value object or a closed set has one entry, its name, kind, signature, role (refused,
seam, or answered elsewhere) and contract. The aspect introduces the member through that entry, which names it, checks
that its template gives it that signature, and marks it; a template whose signature drifts from its entry fails every
build that generates it (LAMA0041 naming the member), rather than let the check look for another method. The check
reads the same entries, under the aspects' own predicates (`ResolveStrategy`, `SupportsSpan`, `SupportsUtf8`,
`HasMinMaxValue`, `DeclaresToString`), called from both sides. The names a value object or a set cannot have
(CMTK1005, CMTK1021) and the interfaces whose explicit implementations are refused are derived from the entries. Not
entries: what no check finds by its name. An explicit implementation is found by the interface mapping, an operator by
its kind, a constructor by CMTK1009 and the companion class by CMTK1007, each marked where it is introduced.

**Every generated member carries `[GeneratedMember(typeof(Contract), "Member")]`**, a sealed attribute in
`CodoMetis.TypeKit.CompilerServices`, the Metalama-free base package: the interface the member implements
(`IParsable<OrderId>.Parse`), or the marker that promises it (`IValidatedValue<Sku, string, SkuFault>.TryFrom`). Not
`[GeneratedCode]`, which Roslyn analyzers skip by default. `ValueObjectDeclaration.IsHandWritten` reads it beside
`[CompilerGenerated]`, and a hand-written one on a value object's or a set's member is CMTK1011, as the generators'
attributes on the type are. The exception is what Metalama cannot mark (above): a value object's `ToString()`, and
`Equals` and `GetHashCode()` over a base record, carry neither attribute, which is what they carried before.

**The attribute and the identity are separable**, and land as two commits in that order: the identity and the
catalogue first, then the attribute, its marks and `IsHandWritten`'s reading of it.

**What changes in a build.** Every regression row of #191 to #197, #205, #226 and #230 holds unchanged. The forms
that failed as LAMA0506, LAMA0531 or LAMA0611 above are CMTK1011 naming the member now, which breaks no build that
built. Five forms that were CMTK1011 build now: a base record's `TryFrom(int?)` over an `int`, a closed set's
`Parse(string, bool?)`, a base record's method named as a generated property or field, and an entry point's overload.
Each was a refusal of code C# compiles beside the generated member, the #230 family, and loosening an Error in a
minor is allowed by decision 34. No base type can carry a generated member today (value objects and closed sets are
sealed, and an abstract one is not generated: CMTK1006, CMTK1014, CMTK1022), so the attribute changes no check's
answer yet; it is what lets one read a member from another assembly without a name.

Rejected: keeping each check's own rule and only sharing the names. The rules disagreed among themselves (the set's
base walk and the value object's on a base's method, `Equals` and `ToNonNullable` on `int?`), so any one shared
answer moves some of them; C#'s is the one the generated code is compiled by. Rejected too: deriving the members by
reflection over the aspect classes' templates, which read names but not the parameter types the aspects set in their
build delegates.

`BuildOutcomeTests` holds the changed forms both ways (`A_hand_written_generated_member_is_refused_by_name`,
`What_the_generators_implement_or_attach_is_refused_by_name_on_a_set`,
`A_member_C_s_rules_keep_apart_from_the_generated_one_builds`), `GeneratedMemberAttributeTests` that every member
with `[CompilerGenerated]` carries the mark and none without it does, in this assembly and the probes', and
`GeneratedSurface.verified.txt` the contract member each names. Seeded defects in the identity, the catalogue and the
mark each fail them (the pull request lists them).
