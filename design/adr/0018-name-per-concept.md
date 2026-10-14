# 0018. One name per concept

Date: 2026-09-27
Status: accepted

Context: 2026-09-27, before the first release, when renaming still costs nothing.

- What a value object holds is *wrapped*: `GeneratedValueObjectAttribute.WrappedType` and
  `TranslatedAsWrappedValueAttribute`, not "underlying", and not `ValueType`, which in .NET means
  a struct and here returned `typeof(string)` for a string-backed value object.
- A value object without rules is *plain*: `IPlainValueObject<TSelf, T>`, which has `From`. It was
  `IValueWrapper`, which named what every value object does.
- The value object's own type parameter is `TSelf` on the self-referencing contracts, as in
  `IParsable<TSelf>`, and `TValueObject` where a helper or attribute names one. Never `TValue`,
  which `KnownGood` used for the value object while "value" means the wrapped value everywhere
  else.
- Delegate parameters: `onSome`/`onNone`, `onSuccess`/`onError`, `selector`, `predicate`,
  `action`, as LINQ names them. They were `fnSome`, `map`, `fn`, `check` and `defaultProvider`.
- Namespaces follow who uses a type ([Packages](../packages.md)). The helpers only generated code calls moved to
  `CodoMetis.TypeKit.CompilerServices`, hidden from IntelliSense, and `Accepted` and `KnownGood`
  became one `GeneratedFactories` whose methods name the exception each entry point throws.
- Internally: `ValueObjectAspect` (was the implementation aspect), `ValueObjectContractAspect`,
  `ValueObjectCompanionAspect`, `ValueObjectKind.Plain`.
