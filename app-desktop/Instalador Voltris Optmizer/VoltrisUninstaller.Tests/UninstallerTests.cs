using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using VoltrisUninstaller.Core;

namespace VoltrisUninstaller.Tests
{
    public class UninstallerTests
    {
        [Fact]
        public void Uninstaller_Should_Create_With_Valid_Options()
        {
            // Arrange
            var logger = new Logger(Path.Combine(Path.GetTempPath(), "test.log"));
            var options = new UninstallOptions { DryRun = true };

            // Act
            var uninstaller = new Uninstaller(logger, options);

            // Assert
            Assert.NotNull(uninstaller);
        }

        [Fact]
        public async Task Uninstaller_Should_Execute_DryRun_Without_Errors()
        {
            // Arrange
            var logger = new Logger(Path.Combine(Path.GetTempPath(), "test.log"));
            var options = new UninstallOptions { DryRun = true, KeepUserData = true };
            var uninstaller = new Uninstaller(logger, options);

            // Act
            var result = await uninstaller.ExecuteAsync();

            // Assert - dry-run shouldn't throw and result should not be null
            Assert.NotNull(result);
        }
    }
}
