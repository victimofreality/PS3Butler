namespace PS3Butler.Models;

public enum JobState
{
    Waiting,
    Ready,
    Running,
    Complete,
    AlreadyExists,
    MissingKey,
    InvalidKey,
    Error
}