/*using System;

 using System.IO;

 using Xunit;

 using VoltrisUninstaller.Core;

 namespace VoltrisUninstaller.Tests 
{
public class DiagnosticScannerTests
 {
[Fact]public void Scanner_Should_Create_Report() {
//Arrangevar logger = new Logger(Path.Combine(Path.GetTempPath(),"test.lo g"));

 var scanner = new DiagnosticScanner(logger);

//Actvar report = scanner.Sca n();

//AssertAssert.NotNull(repor t);

 Assert.NotNull(report.ScanDate);

 Assert.NotNull(report.RegistryEntries);

 Assert.NotNull(report.Shortcuts);

 Assert.NotNull(report.Services);

 }
[Fact]public void Scanner_Should_Export_Report_To_Json() {
//Arrangevar logger = new Logger(Path.Combine(Path.GetTempPath(),"test.lo g"));

 var scanner = new DiagnosticScanner(logger);

 var report = scanner.Scan();

 var outputPath = Path.Combine(Path.GetTempPath(),$"diagnostic - {
Guid.NewGuid()}
.json");

 try {
//Actscanner.ExportReport(report, outputPat h);

//AssertAssert.True(File.Exists(outputPat h));

 var content = File.ReadAllText(outputPath);

 Assert.Contains("ScanDate", content);

 }
 finally {
if(File.Exists(outputPath))File.Delete(outputPath);

 }
 }
 }
 }
*/
