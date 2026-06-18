# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy core projects and the demo code
COPY Morpheo.Sdk/ Morpheo.Sdk/
COPY Morpheo.Core/ Morpheo.Core/
COPY Morpheo.Usb/ Morpheo.Usb/
COPY morpheo-poc/Morpheo.DemoNode/ morpheo-poc/Morpheo.DemoNode/

# Restore dependencies
RUN dotnet restore morpheo-poc/Morpheo.DemoNode/Morpheo.DemoNode.csproj

# Build and publish
RUN dotnet publish morpheo-poc/Morpheo.DemoNode/Morpheo.DemoNode.csproj -c Release -o /app/publish --no-restore

# Final runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

# Expose ports:
#  - 80: User API (Todo CRUD API)
#  - 5000: Morpheo Sync API, SignalR Hub, and Dashboard
EXPOSE 80
EXPOSE 5000

# Set up the application files in /app
WORKDIR /app
COPY --from=build /app/publish .

# Create the data directory and set it as the working directory so
# that both sqlite db and morpheo.runtime.json reside in the mounted volume.
RUN mkdir -p /app/data /app/data/mock-usb
WORKDIR /app/data

ENTRYPOINT ["dotnet", "/app/Morpheo.DemoNode.dll"]
