using Microsoft.VisualStudio.TestTools.UnitTesting;
using AI.FileOrganizer; // Assuming Organize class is in this namespace
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using System; // Required for DateTime

namespace AI.FileOrganizer.Tests
{
    [TestClass]
    public class OrganizeTests
    {
        private Organize _organizer = new Organize(); // Instance of the class to test (if methods aren't static)
                                                   // If GetImageDateTaken is static, we don't need an instance for it.
                                                   // But OrganizeImagesByDateAndContext is an instance method.

        private string _testDir = string.Empty;

        [TestInitialize]
        public void TestInitialize()
        {
            // Create a unique directory for each test run to avoid conflicts
            _testDir = Path.Combine(Path.GetTempPath(), "FileOrganizerTest_" + Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testDir);
        }

        [TestCleanup]
        public void TestCleanup()
        {
            // Clean up the directory after each test
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }

        // --- Tests for GetImageDateTaken (Helper Method) ---

        // GetImageDateTaken is private static, so we need a way to test it.
        // For now, let's assume we can make it internal static for testing or use reflection.
        // Or, we test it indirectly through a public method that uses it, if possible.
        // The subtask asks to test it directly. If it's private, this is tricky without modifying the source.
        // Let's proceed assuming we can call it, perhaps by making it internal for the sake_of_testing or via a public wrapper if that was allowed.
        // For this exercise, I will write the tests as if GetImageDateTaken were callable.
        // If GetImageDateTaken remains private, these tests would need to be adapted or might not be directly possible without source code modification.

        // Helper to call the private static method GetImageDateTaken using reflection
        private static DateTime? InvokeGetImageDateTaken(string filePath)
        {
            var methodInfo = typeof(Organize).GetMethod("GetImageDateTaken", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (methodInfo == null)
            {
                throw new InvalidOperationException("GetImageDateTaken method not found. Ensure it exists and is static.");
            }
            return (DateTime?)methodInfo.Invoke(null, new object[] { filePath });
        }

        [TestMethod]
        public void Test_GetImageDateTaken_ReturnsNullForNonExistentFile()
        {
            string bogusFilePath = Path.Combine(_testDir, "nonexistentfile.jpg");
            var result = InvokeGetImageDateTaken(bogusFilePath);
            Assert.IsNull(result, "Should return null for a non-existent file.");
        }

        [TestMethod]
        public void Test_GetImageDateTaken_FallsBackToFileModifiedDate()
        {
            string tempFilePath = Path.Combine(_testDir, "test_image.jpg");
            // Create a dummy file. Content doesn't matter for this test, only its metadata.
            File.WriteAllBytes(tempFilePath, new byte[] { 0x01, 0x02, 0x03 });

            DateTime knownDate = new DateTime(2023, 01, 15, 10, 30, 00, DateTimeKind.Local);
            File.SetLastWriteTime(tempFilePath, knownDate);

            var result = InvokeGetImageDateTaken(tempFilePath);

            Assert.IsNotNull(result, "Date should not be null as it should fall back to file modified date.");
            // Allow for a small difference due to precision issues or system delays
            Assert.IsTrue((knownDate - result.Value).Duration().TotalSeconds < 1,
                          $"Expected date {knownDate}, but got {result.Value}. Fallback to LastWriteTime failed.");

            // Cleanup is handled by TestCleanup
        }

        // --- Tests for OrganizeImagesByDateAndContext ---

        [TestMethod]
        public async Task Test_OrganizeImagesByDateAndContext_CreatesDateAndExtensionFolders()
        {
            // Arrange
            string sourceDir = Path.Combine(_testDir, "source");
            Directory.CreateDirectory(sourceDir);

            string file1Path = Path.Combine(sourceDir, "image1.jpg");
            string file2Path = Path.Combine(sourceDir, "image2.png");
            File.WriteAllText(file1Path, "dummy jpg content");
            File.WriteAllText(file2Path, "dummy png content");

            DateTime date1 = new DateTime(2023, 10, 20);
            DateTime date2 = new DateTime(2023, 11, 05);
            File.SetLastWriteTime(file1Path, date1);
            File.SetLastWriteTime(file2Path, date2);

            // Act
            var summary = await _organizer.OrganizeImagesByDateAndContext(sourceDir, false, null);

            // Assert
            string expectedDir1 = Path.Combine(sourceDir, "2023-10-20", "jpg");
            string expectedFile1Dest = Path.Combine(expectedDir1, "image1.jpg");
            string expectedDir2 = Path.Combine(sourceDir, "2023-11-05", "png");
            string expectedFile2Dest = Path.Combine(expectedDir2, "image2.png");

            Assert.IsTrue(Directory.Exists(expectedDir1), "Date/extension folder for image1 not created.");
            Assert.IsTrue(File.Exists(expectedFile1Dest), "image1.jpg not moved to correct folder.");
            Assert.IsFalse(File.Exists(file1Path), "Original image1.jpg not removed from source.");

            Assert.IsTrue(Directory.Exists(expectedDir2), "Date/extension folder for image2 not created.");
            Assert.IsTrue(File.Exists(expectedFile2Dest), "image2.png not moved to correct folder.");
            Assert.IsFalse(File.Exists(file2Path), "Original image2.png not removed from source.");

            Assert.AreEqual($"Organized 2 image files into date and context subfolders in {sourceDir}.", summary);
        }

        [TestMethod]
        public async Task Test_OrganizeImagesByDateAndContext_UsesAiContextPlaceholderWhenMultimodalTrue()
        {
            // Arrange
            string sourceDir = Path.Combine(_testDir, "source_multimodal");
            Directory.CreateDirectory(sourceDir);

            string file1Path = Path.Combine(sourceDir, "photo.jpeg");
            File.WriteAllText(file1Path, "dummy jpeg content");
            DateTime date1 = new DateTime(2024, 01, 01);
            File.SetLastWriteTime(file1Path, date1);

            // Act
            var summary = await _organizer.OrganizeImagesByDateAndContext(sourceDir, true, null);

            // Assert
            string expectedDir = Path.Combine(sourceDir, "2024-01-01", "AI_Context"); // As per current implementation
            string expectedFileDest = Path.Combine(expectedDir, "photo.jpeg");

            Assert.IsTrue(Directory.Exists(expectedDir), "Date/AI_Context folder not created.");
            Assert.IsTrue(File.Exists(expectedFileDest), "photo.jpeg not moved to AI_Context folder.");
            Assert.IsFalse(File.Exists(file1Path), "Original photo.jpeg not removed.");
            Assert.AreEqual($"Organized 1 image files into date and context subfolders in {sourceDir}.", summary);
        }

        [TestMethod]
        public async Task Test_OrganizeImagesByDateAndContext_HandlesEmptyDirectory()
        {
            // Arrange
            string sourceDir = Path.Combine(_testDir, "empty_dir");
            Directory.CreateDirectory(sourceDir);

            // Act
            var summary = await _organizer.OrganizeImagesByDateAndContext(sourceDir, false, null);

            // Assert
            Assert.AreEqual("No image files found in the directory.", summary);
        }

        [TestMethod]
        public async Task Test_OrganizeImagesByDateAndContext_HandlesDirectoryWithNoImages()
        {
            // Arrange
            string sourceDir = Path.Combine(_testDir, "no_images_dir");
            Directory.CreateDirectory(sourceDir);
            File.WriteAllText(Path.Combine(sourceDir, "document.txt"), "not an image");
            File.WriteAllText(Path.Combine(sourceDir, "archive.zip"), "also not an image");

            // Act
            var summary = await _organizer.OrganizeImagesByDateAndContext(sourceDir, false, null);

            // Assert
            Assert.AreEqual("No image files found in the directory.", summary);
        }
    }
}
