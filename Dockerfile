FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Careers/Careers.csproj Careers/
RUN dotnet restore Careers/Careers.csproj
COPY Careers/ Careers/
RUN dotnet publish Careers/Careers.csproj -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Careers.dll"]
