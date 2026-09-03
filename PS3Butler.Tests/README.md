# PS3 Butler Tests

This project contains the local automated test suite for PS3 Butler. It does
not require PS3 game images, disc keys, 7-Zip, ps3decrs, or network access.

## Run the tests

From the repository root:

```powershell
dotnet test PS3Butler.slnx
```

## Collect code coverage

```powershell
dotnet test PS3Butler.Tests\PS3Butler.Tests.csproj `
    --collect:"XPlat Code Coverage" `
    --results-directory PS3Butler.Tests\TestResults
```

Coverage result files are ignored by Git.

## Test infrastructure

- `TemporaryDirectory` isolates tests from user settings and files.
- `ParamSfoBuilder` creates small synthetic `PARAM.SFO` fixtures.
- `StubHttpMessageHandler` provides deterministic HTTP responses without
  accessing the network.
- `InlineProgress<T>` makes progress callbacks deterministic in tests.

External-process success, failure, and cancellation behavior will be covered
when the application gains the planned process-runner abstraction. Until then,
the suite avoids launching fake or real operating-system tools.
