// Two fault types of one nesting chain in two namespaces: qualified, both write ShopFault.NotFound.
namespace CodoMetis.TypeKit.AspNetCore.Tests.Billing
{
    public enum ShopFault { NotFound }
}

namespace CodoMetis.TypeKit.AspNetCore.Tests.Shipping
{
    public enum ShopFault { NotFound, Lost }
}
