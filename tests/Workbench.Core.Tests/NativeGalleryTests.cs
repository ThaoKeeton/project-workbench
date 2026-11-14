using Workbench.App;
using Xunit;

namespace Workbench.Core.Tests;

public sealed class NativeGalleryTests
{
    [Theory]
    [InlineData(10, 20, 30, 40, 10, 20, 30, 40)]
    [InlineData(0, 0, 100, 80, 0, 0, 100, 80)]
    [InlineData(-10, -10, 120, 100, 0, 0, 100, 80)]
    [InlineData(-10, 20, 30, 40, 0, 20, 20, 40)]
    [InlineData(90, 20, 30, 40, 90, 20, 10, 40)]
    [InlineData(10, -20, 30, 40, 10, 0, 30, 20)]
    [InlineData(10, 70, 30, 40, 10, 70, 30, 10)]
    public void Intersection_keeps_only_positive_visible_overlap(double x, double y, double width, double height,
        double expectedX, double expectedY, double expectedWidth, double expectedHeight)
    {
        var viewport = new NativeGalleryBounds(0, 0, 100, 80);
        var focus = new NativeGalleryBounds(x, y, width, height);
        var expected = new NativeGalleryBounds(expectedX, expectedY, expectedWidth, expectedHeight);

        Assert.True(NativeGalleryPolicy.TryIntersect(viewport, focus, out var intersection));
        Assert.Equal(expected, intersection);
        Assert.True(NativeGalleryPolicy.TryIntersect(focus, viewport, out var reversed));
        Assert.Equal(expected, reversed);
    }

    [Theory]
    [InlineData(-30, 10, 20, 20)]
    [InlineData(110, 10, 20, 20)]
    [InlineData(10, -30, 20, 20)]
    [InlineData(10, 90, 20, 20)]
    [InlineData(-20, 10, 20, 20)]
    [InlineData(100, 10, 20, 20)]
    [InlineData(10, -20, 20, 20)]
    [InlineData(10, 80, 20, 20)]
    [InlineData(10, 10, 0, 20)]
    [InlineData(10, 10, 20, 0)]
    [InlineData(10, 10, -1, 20)]
    [InlineData(10, 10, 20, -1)]
    [InlineData(10, 10, -1, -1)]
    [InlineData(double.NaN, 10, 20, 20)]
    [InlineData(double.PositiveInfinity, 10, 20, 20)]
    [InlineData(double.NegativeInfinity, 10, 20, 20)]
    [InlineData(10, double.NaN, 20, 20)]
    [InlineData(10, double.PositiveInfinity, 20, 20)]
    [InlineData(10, double.NegativeInfinity, 20, 20)]
    [InlineData(10, 10, double.NaN, 20)]
    [InlineData(10, 10, double.PositiveInfinity, 20)]
    [InlineData(10, 10, double.NegativeInfinity, 20)]
    [InlineData(10, 10, 20, double.NaN)]
    [InlineData(10, 10, 20, double.PositiveInfinity)]
    [InlineData(10, 10, 20, double.NegativeInfinity)]
    public void Outside_touching_empty_and_nonfinite_bounds_have_no_intersection(double x, double y,
        double width, double height)
    {
        var viewport = new NativeGalleryBounds(0, 0, 100, 80);
        var invalid = new NativeGalleryBounds(x, y, width, height);

        Assert.False(NativeGalleryPolicy.TryIntersect(viewport, invalid, out var intersection));
        Assert.Equal(default, intersection);
        Assert.False(NativeGalleryPolicy.TryIntersect(invalid, viewport, out var reversed));
        Assert.Equal(default, reversed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Finite_coordinates_with_overflowed_edges_are_refused(bool horizontal)
    {
        var overflowing = horizontal
            ? new NativeGalleryBounds(double.MaxValue / 2, 0, double.MaxValue, 10)
            : new NativeGalleryBounds(0, double.MaxValue / 2, 10, double.MaxValue);
        var contained = horizontal
            ? new NativeGalleryBounds(double.MaxValue / 2, 0, double.MaxValue / 4, 10)
            : new NativeGalleryBounds(0, double.MaxValue / 2, 10, double.MaxValue / 4);

        Assert.False(NativeGalleryPolicy.TryIntersect(overflowing, contained, out var intersection));
        Assert.Equal(default, intersection);
        Assert.False(NativeGalleryPolicy.TryIntersect(contained, overflowing, out var reversed));
        Assert.Equal(default, reversed);
    }

    [Fact]
    public void Root_page_and_clip_each_limit_the_remaining_focus_bounds()
    {
        var focus = new NativeGalleryBounds(70, 25, 60, 40);
        Assert.True(NativeGalleryPolicy.TryIntersect(focus, new(0, 0, 120, 100), out var inRoot));
        Assert.Equal(new NativeGalleryBounds(70, 25, 50, 40), inRoot);
        Assert.True(NativeGalleryPolicy.TryIntersect(inRoot, new(20, 10, 80, 70), out var inPage));
        Assert.Equal(new NativeGalleryBounds(70, 25, 30, 40), inPage);
        Assert.True(NativeGalleryPolicy.TryIntersect(inPage, new(80, 0, 15, 100), out var inClip));
        Assert.Equal(new NativeGalleryBounds(80, 25, 15, 40), inClip);
    }

    [Fact]
    public void A_clip_can_completely_remove_otherwise_visible_focus_bounds()
    {
        Assert.True(NativeGalleryPolicy.TryIntersect(new(70, 25, 60, 40), new(0, 0, 120, 100), out var inRoot));
        Assert.True(NativeGalleryPolicy.TryIntersect(inRoot, new(20, 10, 80, 70), out var inPage));

        Assert.False(NativeGalleryPolicy.TryIntersect(inPage, new(0, 0, 60, 100), out var inClip));
        Assert.Equal(default, inClip);
    }

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
