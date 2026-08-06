# Building Happy Photon

Install the .NET 10 SDK, then run:

```bash
dotnet restore HappyPhoton.sln --locked-mode
dotnet build HappyPhoton.sln --configuration Release --no-restore
dotnet test HappyPhoton.sln --configuration Release --no-build --no-restore
dotnet run --project HappyPhoton.csproj
```

Windows portable publish:

```bash
dotnet publish HappyPhoton.csproj -p:PublishProfile=win-x64
```

Linux portable publish:

```bash
dotnet publish HappyPhoton.csproj -p:PublishProfile=linux-x64
```

Local Apple Silicon app bundle:

```bash
./scripts/package-macos.sh
```

The local macOS script uses ad-hoc signing for development. Public artifacts
must be Developer ID-signed, notarized, and stapled.
