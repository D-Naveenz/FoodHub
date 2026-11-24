using System.Runtime.CompilerServices;

// Let the isolated test harness recreate generated test databases without weakening the public reset-target guard.
[assembly: InternalsVisibleTo("FoodHub.WinUI.Test")]