<%
On Error Resume Next

' 1. Initialize WMI Connection (Local Object Query - No Network/DCOM Required)
Set objWMIService = GetObject("winmgmts:\\.\root\cimv2")

' 2. Query Local CPU Load Percentage
Set colProcessors = objWMIService.ExecQuery("Select LoadPercentage from Win32_Processor")
For Each objProcessor in colProcessors
    cpuLoad = objProcessor.LoadPercentage
    Exit For
Next

' 3. Query Local Memory Metrics
Set colOS = objWMIService.ExecQuery("Select TotalVisibleMemorySize, FreePhysicalMemory from Win32_OperatingSystem")
For Each objOS in colOS
    totalMemory = objOS.TotalVisibleMemorySize
    freeMemory = objOS.FreePhysicalMemory
    Exit For
Next

' 4. Calculate Used Memory Percentage
If totalMemory > 0 Then
    memUsage = Round(((totalMemory - freeMemory) / totalMemory) * 100, 1)
Else
    memUsage = 0
End If

' 5. Fallback Defaulting (If WMI counters are temporarily locked)
If IsNull(cpuLoad) Or cpuLoad = "" Then cpuLoad = 0
If IsNull(memUsage) Or memUsage = "" Then memUsage = 0

' 6. Define Resource Threshold Limits (CPU > 85% OR Memory > 90%)
cpuThreshold = 85
memThreshold = 90

Response.ContentType = "application/json"

' 7. Evaluate Thresholds & Return HTTP Headers + Payload
If cpuLoad > cpuThreshold Or memUsage > memThreshold Then
    ' Threshold breached: Return HTTP 503 to signal F5 BIG-IP to drain/reroute traffic
    Response.Status = "503 Service Unavailable"
    Response.Write "{""status"":""Unhealthy"", ""cpu"":""" & cpuLoad & "%"", ""memory"":""" & memUsage & "%""}"
Else
    ' System healthy: Return HTTP 200 OK for standard load balancing
    Response.Status = "200 OK"
    Response.Write "{""status"":""Healthy"", ""cpu"":""" & cpuLoad & "%"", ""memory"":""" & memUsage & "%""}"
End If
%>
