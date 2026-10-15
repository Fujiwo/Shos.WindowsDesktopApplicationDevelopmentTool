namespace WinDevBridge.Tests;

public class VSTestCommandTests
{
    const string VSTest = @"C:\VS\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe";

    [Fact]
    public void TestWithoutFilterRunsAllTestsInDll()
    {
        var command = VSTestCommand.Test(VSTest, @"C:\ws\x64\Debug\App.Tests.dll", filter: null);

        Assert.Equal(VSTest, command.FileName);
        Assert.Equal([@"C:\ws\x64\Debug\App.Tests.dll"], command.Arguments);
    }

    [Fact]
    public void TestWithFilterPassesFilterAsOneArgument()
        => Assert.Equal(
            ["App.Tests.dll", "/TestCaseFilter:FullyQualifiedName~Temperature & Priority=1"],
            VSTestCommand.Test(VSTest, "App.Tests.dll", "FullyQualifiedName~Temperature & Priority=1").Arguments);

    [Fact]
    public void TestWithBlankFilterRunsAllTests()
        => Assert.Equal(["App.Tests.dll"], VSTestCommand.Test(VSTest, "App.Tests.dll", " ").Arguments);
}
