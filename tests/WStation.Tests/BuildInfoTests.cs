using WStation.Core.Common;
using Xunit;

namespace WStation.Tests
{
    public class BuildInfoTests
    {
        [Fact]
        public void A_verzio_a_kozos_verzioszambol_jon()
        {
            Assert.StartsWith("3.", BuildInfo.Version);
            Assert.DoesNotContain("+", BuildInfo.Version);
        }
    }
}
