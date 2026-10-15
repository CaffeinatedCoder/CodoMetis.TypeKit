---
verified: 2026-10-14
pins:
  - external AdvancedGenericTypeConstraints 0.4.1
  - external RossLean.GenericsAnalyzer 1.4.0
source: the NuGet pages, READMEs and rule documentation of AdvancedGenericTypeConstraints 0.4.1 and RossLean.GenericsAnalyzer 1.4.0, and the csharplang discussion 1004 and issues 742, 3013 and 9641
turned-on: decisions 79 and 81 (the type-argument constraint attributes of CMTK0010, [Distinct] and [Unrelated]), which found no library offering them
test: none, a competitor's feature, read from its source and package
---

# KB-024. No .NET library constrains a type argument to be abstract, sealed, a record or concrete, or two arguments to differ

The two nearest libraries are attributes with a Roslyn analyzer, checked statically:

- **AdvancedGenericTypeConstraints 0.4.1** (2026-04-15; formerly OpenGenericConstraints): `MustImplementOpenGeneric`,
  `MustNotImplementOpenGeneric`, `MustHaveAttribute`, `MustMatchTypeName`, `MustMatchAssemblyNameOf`, and for
  `System.Type` parameters `MustBeOpenGenericType`, `MustBeReferenceType`, `MustBeAssignableTo`. A parameter
  forwarded to another generic passes when it carries an equal or stricter constraint. Diagnostics AGTC00x.
- **RossLean.GenericsAnalyzer 1.4.0** (2024-02-19): `ProhibitedTypes`, `ProhibitedBaseTypes`, `PermittedTypes`,
  `PermittedBaseTypes`, `OnlyPermitSpecifiedTypes`, and type constraint profiles. Its "Distinct" (GA0028) makes
  profiles on one parameter mutually exclusive; it does not keep the arguments of two parameters apart. GA0017
  checks a forwarded parameter's explicit constraints.

Neither offers abstract, sealed, record or concrete, nor distinct or unrelated arguments. In the language, a
non-abstract constraint (discussion 1004, issue 742), a record constraint (9641) and sealed classes as constraints
(3013) are asked for and not accepted; CS0701 refuses a sealed class as a constraint. AdvancedGenericTypeConstraints
moves fast (0.1.0 to 0.4.1 in two weeks): read it again before a release claims this.
