# AZPCloud Careers

Public job list and applications for AZPCloud. An administrator uploads a Word job file, applicants register and apply, and the application PDF plus resume are emailed to the addresses that are marked to receive them.

The live site is https://careers.azpcloud.com. Its database, mail connection, and admin password are set in Azure. They are not in this repository.

A job file can ask for short text, long text, a dropdown, yes/no, or multiple choice. Multiple choice is checkboxes. Separate its choices with semicolons.

## Run locally

Install the .NET 10 SDK and Docker.

Start MongoDB:

```bash
docker run -d --name careers-mongo -p 27017:27017 -v careers-mongo:/data/db mongo:8
```

The app uses `mongodb://localhost:27017` and the database `careers`.

Set the development admin password on your machine. It is not stored in this repository.

```bash
dotnet user-secrets set "SeedAdmin:Password" "your-password" --project Careers
```

The development admin email is `admin@azpcloud.com`. That account is created on first run only when the password above is set and the email is not already in the database.

```bash
dotnet run --project Careers --launch-profile http
```

Open http://localhost:5188.

## Mail

Checked administrator addresses receive each application PDF and resume. Sending uses Azure Communication Services when `Mail:AzureConnectionString` is set. The from address is `jobs@careers.azpcloud.com`. Until that connection string is set, this Windows computer sends through Outlook. SMTP is used instead when `Mail:Host` is set.

Resumes and generated PDFs are saved under `App_Data` and are not stored in MongoDB.
