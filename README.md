# AI.FileOrganizer

AI.FileOrganizer is an AI-powered command-line tool for organizing and categorizing files and folders on your computer. It leverages local LLMs (such as Llama/Llava/Gemma) via [LLamaSharp](https://github.com/SciSharp/LLamaSharp) and [Microsoft Semantic Kernel](https://github.com/microsoft/semantic-kernel) to interpret natural language instructions and automate file management tasks.

## Features

- **Natural Language CLI:** Interact with the tool using plain English commands.
- **File Listing & Moving:** List files in directories and move files between folders.
- **Categorization:** Categorize files by extension, name context, or content context.
- **Image Organization:** Categorize and organize images by extension or, with multimodal models, by image content.
- **Folder Organization:** Organize folders by name patterns or by the number of files they contain.
- **Extensible:** Easily add new organization or categorization strategies.
- **Confirmation Prompts:** The CLI will ask for confirmation before executing any file-changing action.

## Project Structure

- `AI.FileOrganizer`: Core library with file/folder organization logic.
- `AI.FileOrganizer.CLI`: Command-line interface, model management, and chat loop.

## Getting Started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- A compatible Llama/Llava/Gemma model file (see below)
- (Optional) CUDA or Vulkan drivers for GPU acceleration

### Build

```sh
dotnet build
```

### Configuration

Set up your model paths using [user secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets):

```sh
dotnet user-secrets set "ModelPath" "<path-to-your-model>"
dotnet user-secrets set "MultiModalProj" "<path-to-your-multimodal-proj>" # Optional, for multimodal models
```

### Run

```sh
dotnet run --project AI.FileOrganizer.CLI
```

### Example Usage

```
> List files in Downloads
> Move file report.pdf to Documents
> Organize files in Desktop by extension
> Categorize images in Pictures by content
```

The assistant will interpret your intent and prompt for confirmation before making changes.

### Example Prompts

When you type a command, it's automatically formatted for the Gemma model like this:

```
list Downloads files
```

You can also use custom file paths:

```
move C:\Work\Reports\2025\project_report.pdf to D:\Backup\Archive
```

For image categorization (using multimodel vision), the format includes image data:

```
categorize images in my <path>
```

```
organize images in my <path>
```

## Supported Commands

- `[LIST FILES] {directory}`
- `[MOVE FILE] {sourceFilePath} {destinationDirectory}`
- `[CATEGORIZE FILES] {directory}`
- `[ORGANIZE FILES] {directory}`
- `[CATEGORIZE BY NAME CONTEXT] {directory}`
- `[CATEGORIZE BY CONTENT CONTEXT] {directory}`
- `[CATEGORIZE IMAGES BY CONTEXT] {directory}`
- `[COUNT PREVIOUS FILES]`

## Recommended Multimodal Models

For best results with image and content-based categorization, use one of these GGUF models (download and set the path in your user secrets):

- [google/gemma-3-4b-it-qat-q4_0-gguf](https://huggingface.co/google/gemma-3-4b-it-qat-q4_0-gguf/tree/main)
- [unsloth/gemma-3-4b-it-GGUF](https://huggingface.co/unsloth/gemma-3-4b-it-GGUF/tree/main)

## License

This project is licensed under the [MIT License](LICENSE.txt).

---

*Powered by LLamaSharp and Microsoft Semantic Kernel.*