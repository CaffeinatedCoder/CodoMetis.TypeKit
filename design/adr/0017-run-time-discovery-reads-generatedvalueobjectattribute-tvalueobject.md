# 0017. Run-time discovery reads `GeneratedValueObjectAttribute<TValueObject, T>`

Date: 2026-09-27
Status: accepted

Context: 2026-09-27, [Discovery](../discovery.md), [Native AOT](../native-aot.md).

,
which the generators put on every value object, not `GetInterfaces()`. Measured under Native AOT:
the interface was gone from every value object reached through a property, and the OpenAPI
satellite described each as `{}`. The attribute's constraints are the interfaces, so what a value
object is has not changed; its `Accept` hands the two types to a visitor as type arguments, which
is how the EF satellite makes a converter without `MakeGenericType`.
