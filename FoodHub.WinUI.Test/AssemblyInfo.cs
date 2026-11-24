using Microsoft.VisualStudio.TestTools.UnitTesting;

// Database tests intentionally create their own internal registration race. Keep test cases
// sequential so native-host lifetime and SQL cleanup remain easy to follow and diagnose.
[assembly: DoNotParallelize]
