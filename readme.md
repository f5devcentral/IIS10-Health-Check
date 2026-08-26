# IIS 10 Health Check Sidecar (.NET 8 Web API)

A lightweight, self-contained **ASP.NET Core (.NET 8) Web API** sidecar application designed to monitor Windows Server CPU and memory metrics in real-time and provide endpoint status probes (`/api/health`) for load balancers such as **F5 BIG-IP**.

---

## Prerequisites (What to Install First)

Before starting, ensure your Windows Server has the required runtime installed so IIS can run ASP.NET Core applications.

### 1. Install the .NET 8 Hosting Bundle
1. Go to Microsoft's download page for the [.NET 8.0 Hosting Bundle](https://dotnet.microsoft.com/en-us/download/dotnet/8.0).
2. Download and run the installer (includes the ASP.NET Core Runtime and the `AspNetCoreModuleV2` for IIS).

### 2. Restart IIS
Open Command Prompt (CMD) as Administrator and execute:
```cmd
iisreset
```

---

## Repository Architecture & Code Structure

```
├── HealthCheckSidecar.csproj    # .NET 8 Web API Project File
├── Program.cs                   # Application entrypoint & health routes (/api/health)
├── Models/
│   └── HealthOptions.cs         # Strong-typed threshold settings
├── Services/
│   └── MetricsCollectorService.cs # Background service sampling live CPU & Memory
├── appsettings.json             # Resource threshold configuration
├── web.config                   # IIS AspNetCoreModuleV2 handler configuration
├── health.asp                   # Classic ASP legacy reference script
└── readme.md                    # Documentation
```

---

## Build & Publish

To compile and publish the sidecar application for deployment:

1. Clone or download this repository.
2. Open a Command Prompt or Terminal in the repository folder.
3. Run the publish command:
   ```cmd
   dotnet publish -c Release -o C:\inetpub\HealthCheckSidecar
   ```
This compiles `HealthCheckSidecar.dll` and outputs all required runtime assets to `C:\inetpub\HealthCheckSidecar`.

---

## Step 1: Configuration (`appsettings.json`)

Edit `appsettings.json` in your deployment directory to customize resource thresholds:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "HealthThresholds": {
    "MaxCpuPercentage": 85.0,
    "MaxMemoryPercentage": 90.0,
    "TotalSystemRamGb": 16.0
  }
}
```

* **MaxCpuPercentage**: HTTP 503 is returned if server CPU usage exceeds this threshold (e.g. `85.0`%).
* **MaxMemoryPercentage**: HTTP 503 is returned if server Memory usage exceeds this threshold (e.g. `90.0`%).

---

## Step 2: Configure the Sidecar Site in IIS 10

```mermaid
graph TD
    A[Open IIS Manager] --> B[Create App Pool: HealthCheckPool]
    B --> C[Set CLR to No Managed Code]
    C --> D[Set AlwaysRunning & Idle Timeout 0]
    D --> E[Add Website on Port 8080]
    E --> F[Point Physical Path to C:\inetpub\HealthCheckSidecar]
```

### 1. Create a Dedicated Application Pool
1. Open IIS Manager (`inetmgr`).
2. Right-click **Application Pools** -> **Add Application Pool...**
3. Name: `HealthCheckPool`
4. Set **.NET CLR version** to **No Managed Code** (required for ASP.NET Core In-Process hosting).
5. Click **OK**.
6. Select `HealthCheckPool`, click **Advanced Settings...**:
   * Change **Start Mode** from `OnDemand` to `AlwaysRunning`.
   * Change **Idle Time-out (minutes)** from `20` to `0`.
7. Click **OK**.

### 2. Create the Website
1. Right-click **Sites** -> **Add Website...**
2. **Site name:** `HealthCheckSidecar`
3. **Application pool:** `HealthCheckPool`
4. **Physical path:** `C:\inetpub\HealthCheckSidecar`
5. **Binding:** Set Port to `8080` (or your dedicated management port).
6. Click **OK**.

---

## Step 3: Test and Verify Locally

1. Open a browser on the server or run `curl`:
   ```bash
   curl -i http://localhost:8080/api/health
   ```
2. Response when Healthy (`HTTP 200 OK`):
   ```json
   {
       "status": "Healthy",
       "cpu": "12.4%",
       "memory": "48.2%"
   }
   ```
3. If CPU or Memory exceeds thresholds, the endpoint automatically returns `HTTP 503 Service Unavailable`:
   ```json
   {
       "status": "Unhealthy",
       "cpu": "89.1%",
       "memory": "48.2%"
   }
   ```

---

## Step 4: Secure the Endpoint for F5 BIG-IP Only

1. In IIS Manager, select `HealthCheckSidecar`.
2. Double-click **IP Address and Domain Restrictions**.
3. Click **Add Allow Entry...** and add your BIG-IP Self-IP addresses.
4. Click **Edit Feature Settings...** and set **Access for unspecified clients** to `Forbidden` or `Abort`.

---

## Monitor with BIG-IP

Configure an **Alias Service Port** (`8080`) monitor on BIG-IP:

```mermaid
sequenceDiagram
    autonumber
    actor Client
    participant F5 as F5 BIG-IP
    participant IIS as IIS 10 Server
    
    rect rgb(240, 248, 255)
    note right of F5: Active Monitoring Channel
    F5->>IIS: Health Probe (Port 8080 GET /api/health)
    IIS-->>F5: HTTP/1.1 200 OK (Healthy)
    end

    rect rgb(240, 255, 240)
    note right of F5: Production Traffic Channel
    Client->>F5: Request Site (Port 80/443)
    F5->>IIS: Forward connection to Pool Member (Port 80/443)
    end
```

If the CPU or Memory threshold is breached:
1. The background service registers the high resource consumption.
2. The probe on Port `8080` receives `HTTP/1.1 503 Service Unavailable`.
3. BIG-IP marks the pool member down and reroutes production traffic away from the overloaded server.
