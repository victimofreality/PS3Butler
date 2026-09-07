namespace PS3Butler.Models;

public enum JobStage
{
    None,
    Waiting,
    Downloading,
    Preflight,
    InspectingArchive,
    ExtractingArchive,
    Decrypting,
    ReadingMetadata,
    CreatingJbFolder,
    Copying,
    CleaningUp,
    Complete,
    Error
}