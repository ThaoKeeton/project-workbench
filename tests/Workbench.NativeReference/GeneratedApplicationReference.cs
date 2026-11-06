namespace Workbench.App;

// Compile-only stand-in for the member emitted by the Windows XAML compiler.
// This project is a library and cannot start the application or validate XAML/PRI/bootstrap.
public sealed partial class WorkbenchApplication
{
    private void InitializeComponent() => throw new PlatformNotSupportedException("Reference compilation only; build and run SDK-generated App.xaml on Windows.");
}
