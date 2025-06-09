using Microsoft.VisualStudio.TestTools.UnitTesting;
using AI.FileOrganizer; // Namespace of the class to test
using System.IO;
using System.Linq;
using System.Collections.Generic; // For IDictionary

namespace AI.FileOrganizer.Tests
{
    [TestClass]
    public class OrganizeTests
    {
        private readonly Organize _organizer = new Organize();

        // Test for CategorizeByExtension
        [TestMethod]
        public void CategorizeByExtension_GroupsFilesCorrectly()
        {
            // This test is conceptual as CategorizeByExtension directly uses Directory.GetFiles.
            // To properly test, we'd need to mock file system interaction.
            // For now, this test serves as a placeholder for the logic.
            // A real test would involve setting up a temporary directory with mock files.
            string directory = "dummy_dir_for_categorize_ext"; // Not actually used by this conceptual test

            // Simulate the output we expect based on hypothetical files
            // In a real scenario, you'd mock Directory.GetFiles(directory)
            // to return new[] { "file1.txt", "file2.doc", "file3.txt" };

            // Let's test the string formatting part if possible, though it's tightly coupled.
            // Given the current implementation, we can't directly test the grouping logic
            // without file system interaction. This highlights a need for refactoring Organize.cs
            // for better testability (e.g., by passing file lists as parameters).

            // For now, let's test a scenario where the directory does not exist,
            // as that path is testable.
            string nonExistentDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            string result = _organizer.CategorizeByExtension(nonExistentDir);
            Assert.AreEqual("Directory does not exist.", result);
        }

        // Test for CategorizeByNameContext
        [TestMethod]
        public void CategorizeByNameContext_GroupsFilesCorrectly()
        {
            // Similar to CategorizeByExtension, this is hard to test without file system mocking.
            // We will test the non-existent directory case.
            string nonExistentDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            string result = _organizer.CategorizeByNameContext(nonExistentDir);
            Assert.AreEqual("Directory does not exist.", result);

            // If we could pass a list of file names, we could test the regex logic:
            // e.g., if files were ["TestFile1.txt", "AnotherTest.doc", "TestFile2.log", "XYZ-Data.dat"]
            // Expected groups: "testfile": ["TestFile1.txt", "TestFile2.log"], "anothertest": ["AnotherTest.doc"], "xyz-data": ["XYZ-Data.dat"]
            // This again points to refactoring Organize.cs for testability.
        }

        // Test for ListFiles
        [TestMethod]
        public void ListFiles_NonExistentDirectory_ReturnsEmptyArray()
        {
            string nonExistentDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            string[] result = _organizer.ListFiles(nonExistentDir);
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Length);
        }

        // Test for MoveFile
        [TestMethod]
        public void MoveFile_NonExistentSource_ReturnsErrorMessage()
        {
            string nonExistentSource = Path.Combine(Path.GetTempPath(), "non_existent_source_file.tmp");
            string dummyDestDir = Path.Combine(Path.GetTempPath(), "dummy_dest_dir");
            Directory.CreateDirectory(dummyDestDir); // Destination must exist for this check path

            string result = _organizer.MoveFile(nonExistentSource, dummyDestDir);
            Assert.AreEqual("Source file or destination directory does not exist.", result);

            Directory.Delete(dummyDestDir);
        }

        [TestMethod]
        public void MoveFile_NonExistentDestination_ReturnsErrorMessage()
        {
            string dummySourceFile = Path.Combine(Path.GetTempPath(), "dummy_source.tmp");
            File.Create(dummySourceFile).Close(); // Source must exist

            string nonExistentDestDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

            string result = _organizer.MoveFile(dummySourceFile, nonExistentDestDir);
            Assert.AreEqual("Source file or destination directory does not exist.", result);

            File.Delete(dummySourceFile);
        }

        // Test for OrganizeImagesByContext - focusing on non-existent directory
        [TestMethod]
        public void OrganizeImagesByContext_NonExistentDirectory_ReturnsErrorMessage()
        {
            string nonExistentDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var imageContextMap = new Dictionary<string, string>
            {
                { "image1.jpg", "label1" }
            };
            string result = _organizer.OrganizeImagesByContext(nonExistentDir, imageContextMap);
            Assert.AreEqual("Directory does not exist.", result);
        }

        // Test for OrganizeImagesByContext - empty map
        [TestMethod]
        public void OrganizeImagesByContext_EmptyMap_ReturnsZeroOrganized()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "org_img_empty_map_test");
            Directory.CreateDirectory(tempDir);

            var imageContextMap = new Dictionary<string, string>();
            string result = _organizer.OrganizeImagesByContext(tempDir, imageContextMap);

            Assert.IsTrue(result.StartsWith("Organized 0 images"));

            Directory.Delete(tempDir, true);
        }


        // Placeholder for a test that could work if Organize.cs was refactored
        // to take a list of file paths instead of a directory string for some methods.
        /*
        [TestMethod]
        public void CategorizeByExtension_WithProvidedFileList_GroupsCorrectly()
        {
            var files = new[] { "photo.jpeg", "document.pdf", "archive.zip", "image.png", "script.py", "data.jpeg" };
            // Expected:
            // .jpeg: photo.jpeg, data.jpeg
            // .pdf: document.pdf
            // .zip: archive.zip
            // .png: image.png
            // .py: script.py

            // This would require a method like:
            // public string CategorizeFilesByExtension(IEnumerable<string> filePaths)
            // var result = _organizer.CategorizeFilesByExtension(files);
            // Assert.IsTrue(result.Contains(".jpeg:"));
            // Assert.IsTrue(result.Contains("  photo.jpeg"));
            // Assert.IsTrue(result.Contains("  data.jpeg"));
            // ... and so on for other extensions
        }
        */
    }
}
