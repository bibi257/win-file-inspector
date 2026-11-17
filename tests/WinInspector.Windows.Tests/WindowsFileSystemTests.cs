using WinInspector.Windows.Fs;
using Xunit;

namespace WinInspector.Windows.Tests;

public sealed class WindowsFileSystemTests
{
    [Fact]
    public void EnumerateReturnsFileMetadata()
    {
        var root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var file=Path.Combine(root,"fixture.txt");File.WriteAllText(file,"test");
            var entry=new WindowsFileSystem().Enumerate(root).Single();
            Assert.Equal(file,entry.FullPath);Assert.Equal(4,entry.Length);Assert.False(entry.IsDirectory);
        }
        finally{Directory.Delete(root,true);}
    }
}
