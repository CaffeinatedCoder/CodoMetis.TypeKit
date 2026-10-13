# 0055. A component name two types claim is refused; a qualified fault name follows the nesting chain

Date: 2026-10-13
Status: accepted

Context: 2026-10-13, the second outside review of 2.0.0; [OpenAPI](../openapi.md).

[Decision 24](0024-nested-value-objects-named-after-nesting.md) names a nested type's component after
its nesting chain, `Order.Id` as `OrderId`. A nested closed set `Order.Status` beside a top-level
`OrderStatus` then got one component, which described whichever ASP.NET met first, and the property
typed `Order.Status` referenced the other's names (measured, on the JIT and under Native AOT). CMTK1007
catches the value-object case within one compilation only, through the companion classes; a closed
set has none, and two assemblies never meet at compile time. Decision 24's names stay, since value
objects' component names shipped in 1.x. Generating a document in which a name this package gave is
also another type's throws an `InvalidOperationException` naming both, per document. Two types this
package does not rename stay ASP.NET's business.

For the same reason, `FaultResponseOptions.QualifiedFaultNames` names an enum after its nesting
chain with dots, `Billing.Fault.NotFound`, as a closed set's `QualifiedName` does; it had used the
simple name, so `Billing.Fault` and `Shipping.Fault` sent one value. The fault responses are new in
2.0.0.
