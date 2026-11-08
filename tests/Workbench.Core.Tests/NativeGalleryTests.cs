using Workbench.App;
using Xunit;

namespace Workbench.Core.Tests;

public sealed class NativeGalleryTests
{
    [Fact]
    public void Real_nonuniform_opaque_pixels_are_accepted()
    {
        Assert.True(NativeGalleryPolicy.IsValidFrame([3, 7, 11, 255, 23, 37, 41, 255], 2, 1));
    }

    [Theory]
    [InlineData(0, 1, 8)]
    [InlineData(-1, 1, 8)]
    [InlineData(4097, 1, 8)]
    [InlineData(1, 4097, 8)]
    [InlineData(int.MaxValue, int.MaxValue, 8)]
    [InlineData(2, 1, 7)]
    [InlineData(2, 1, 9)]
    public void Invalid_dimensions_or_length_are_refused(int width, int height, int length)
    {
        Assert.False(NativeGalleryPolicy.IsValidFrame(new byte[length], width, height));
    }

    [Fact]
    public void Uniform_and_fully_transparent_frames_are_refused()
    {
        Assert.False(NativeGalleryPolicy.IsValidFrame([3, 7, 11, 255, 3, 7, 11, 255], 2, 1));
        Assert.False(NativeGalleryPolicy.IsValidFrame([3, 7, 11, 0, 23, 37, 41, 0], 2, 1));
    }
}
