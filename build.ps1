dotnet publish `
    src/Telegrafer `
    --configuration Release `
    --runtime win-x64 `
    --sc -p:PublishSingleFile=true `
    --output ./bin/win-x64/

dotnet publish `
    src/Telegrafer `
    --configuration Release `
    --runtime linux-x64 `
    --sc -p:PublishSingleFile=true `
    --output ./bin/linux-x64/

dotnet publish `
    src/Telegrafer `
    --configuration Release `
    --runtime osx-x64 `
    --sc -p:PublishSingleFile=true `
    --output ./bin/osx-x64/
