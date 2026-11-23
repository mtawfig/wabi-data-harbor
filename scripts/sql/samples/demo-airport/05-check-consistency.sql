-- ==============================================================================================================
-- Demo Airport sample databases — consistency check. Run after 01-04. Every line must show Result = OK.
-- ==============================================================================================================
SET NOCOUNT ON;
SELECT c.CheckName, c.Actual, c.Expected, CASE WHEN c.Passed = 1 THEN 'OK' ELSE 'FAILED' END AS Result
FROM (
    SELECT 1 AS Ord, 'AODB: flights' AS CheckName, CAST(COUNT(*) AS varchar(20)) AS Actual, '255336' AS Expected, CASE WHEN COUNT(*) = 255336 THEN 1 ELSE 0 END AS Passed FROM DemoAirport_AODB.ops.Flight
    UNION ALL SELECT 2, 'AODB: arrivals equal departures', CAST(SUM(CASE WHEN Direction = 'A' THEN 1 ELSE 0 END) AS varchar(20)), '127668', CASE WHEN SUM(CASE WHEN Direction = 'A' THEN 1 ELSE 0 END) = 127668 THEN 1 ELSE 0 END FROM DemoAirport_AODB.ops.Flight
    UNION ALL SELECT 3, 'AODB: first flight date', CONVERT(varchar(10), MIN(FlightDate), 23), '2023-01-01', CASE WHEN MIN(FlightDate) = '2023-01-01' THEN 1 ELSE 0 END FROM DemoAirport_AODB.ops.Flight
    UNION ALL SELECT 4, 'AODB: airlines other than Demo Airlines', CAST(COUNT(DISTINCT f.AirlineId) AS varchar(20)), 'at least 45', CASE WHEN COUNT(DISTINCT f.AirlineId) >= 45 THEN 1 ELSE 0 END
              FROM DemoAirport_AODB.ops.Flight f JOIN DemoAirport_AODB.ref.Airline a ON a.AirlineId = f.AirlineId WHERE a.IsBaseCarrier = 0
    UNION ALL SELECT 5, 'AODB: every flight has its linked flight', CAST(COUNT(*) AS varchar(20)), '0 missing', CASE WHEN COUNT(*) = 0 THEN 1 ELSE 0 END FROM DemoAirport_AODB.ops.Flight WHERE LinkedFlightId IS NULL
    UNION ALL SELECT 6, 'AODB: operated flights without a load record', CAST(COUNT(*) AS varchar(20)), '0', CASE WHEN COUNT(*) = 0 THEN 1 ELSE 0 END
              FROM DemoAirport_AODB.ops.Flight f WHERE f.FlightStatus IN ('Arrived', 'Departed') AND NOT EXISTS (SELECT 1 FROM DemoAirport_AODB.ops.FlightLoad l WHERE l.FlightId = f.FlightId)
    UNION ALL SELECT 7, 'HCM: employees', CAST(COUNT(*) AS varchar(20)), '1864', CASE WHEN COUNT(*) = 1864 THEN 1 ELSE 0 END FROM DemoAirport_HCM.hr.Employee
    UNION ALL SELECT 8, 'HCM: employees in service', CAST(COUNT(*) AS varchar(20)), 'at least 1252', CASE WHEN COUNT(*) >= 1252 THEN 1 ELSE 0 END FROM DemoAirport_HCM.hr.Employee WHERE EmploymentStatus = 'Active'
    UNION ALL SELECT 9, 'HCM: leavers have a separation record', CAST(COUNT(*) AS varchar(20)), '0 missing', CASE WHEN COUNT(*) = 0 THEN 1 ELSE 0 END
              FROM DemoAirport_HCM.hr.Employee e WHERE e.EmploymentStatus <> 'Active' AND NOT EXISTS (SELECT 1 FROM DemoAirport_HCM.hr.Separation s WHERE s.EmployeeId = e.EmployeeId)
    UNION ALL SELECT 10, 'HCM: payslips outside the employment period', CAST(COUNT(*) AS varchar(20)), '0', CASE WHEN COUNT(*) = 0 THEN 1 ELSE 0 END
              FROM DemoAirport_HCM.pay.Payslip p JOIN DemoAirport_HCM.pay.PayrollRun r ON r.PayrollRunId = p.PayrollRunId JOIN DemoAirport_HCM.hr.Employee e ON e.EmployeeId = p.EmployeeId
              WHERE e.HireDate > r.PeriodEnd OR e.SeparationDate < r.PeriodStart
    UNION ALL SELECT 11, 'HCM: attendance outside the employment period', CAST(COUNT(*) AS varchar(20)), '0', CASE WHEN COUNT(*) = 0 THEN 1 ELSE 0 END
              FROM DemoAirport_HCM.ta.Attendance a JOIN DemoAirport_HCM.hr.Employee e ON e.EmployeeId = a.EmployeeId WHERE a.WorkDate < e.HireDate OR a.WorkDate > e.SeparationDate
    UNION ALL SELECT 12, 'Assets: technicians not in HCM', CAST(COUNT(*) AS varchar(20)), '0', CASE WHEN COUNT(*) = 0 THEN 1 ELSE 0 END
              FROM DemoAirport_Assets.mnt.Technician t WHERE NOT EXISTS (SELECT 1 FROM DemoAirport_HCM.hr.Employee e WHERE e.EmployeeNumber = t.EmployeeNumber)
    UNION ALL SELECT 13, 'Assets: work given to someone not employed that day', CAST(COUNT(*) AS varchar(20)), '0', CASE WHEN COUNT(*) = 0 THEN 1 ELSE 0 END
              FROM DemoAirport_Assets.mnt.WorkOrder w JOIN DemoAirport_Assets.mnt.Technician t ON t.TechnicianId = w.AssignedTechnicianId
              WHERE CAST(w.StartDateTime AS date) < t.HireDate OR CAST(w.StartDateTime AS date) > t.SeparationDate
    UNION ALL SELECT 14, 'Assets: completed work orders without a technician', CAST(COUNT(*) AS varchar(20)), '0', CASE WHEN COUNT(*) = 0 THEN 1 ELSE 0 END
              FROM DemoAirport_Assets.mnt.WorkOrder WHERE WorkOrderStatus = 'Completed' AND AssignedTechnicianId IS NULL
    UNION ALL SELECT 15, 'RMS: flight legs equal AODB flights', CAST(COUNT(*) AS varchar(20)), '255336', CASE WHEN COUNT(*) = 255336 THEN 1 ELSE 0 END FROM DemoAirport_RMS.[plan].FlightLeg
    UNION ALL SELECT 16, 'RMS: flights without a stand allocation', CAST(COUNT(*) AS varchar(20)), '0', CASE WHEN COUNT(*) = 0 THEN 1 ELSE 0 END
              FROM DemoAirport_RMS.[plan].FlightLeg f WHERE NOT EXISTS (SELECT 1 FROM DemoAirport_RMS.[plan].StandAllocation a WHERE a.ArrivalFlightLegId = f.FlightLegId OR a.DepartureFlightLegId = f.FlightLegId)
    UNION ALL SELECT 17, 'RMS: equipment not in the asset register', CAST(COUNT(*) AS varchar(20)), '0', CASE WHEN COUNT(*) = 0 THEN 1 ELSE 0 END
              FROM DemoAirport_RMS.res.Equipment e WHERE NOT EXISTS (SELECT 1 FROM DemoAirport_Assets.eam.Asset a WHERE a.AssetTag = e.EquipmentCode)
    UNION ALL SELECT 18, 'RMS: tasks given to someone not on shift that day', CAST(COUNT(*) AS varchar(20)), '0', CASE WHEN COUNT(*) = 0 THEN 1 ELSE 0 END
              FROM DemoAirport_RMS.ops.Task t WHERE t.StaffId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM DemoAirport_RMS.staff.ShiftAssignment s WHERE s.StaffId = t.StaffId
                   AND ISNULL(t.ActualStart, t.PlannedStart) >= s.ShiftStart AND ISNULL(t.ActualStart, t.PlannedStart) < DATEADD(hour, 1, s.ShiftEnd))
) c ORDER BY c.Ord;
