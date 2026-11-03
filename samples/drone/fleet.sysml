/*
 * One drone in service: this battery, this controller, these settings.
 */
package DroneFleet {
    private import DroneStructure::*;

    individual part def FleetDrone :> Drone;

    individual part unit7 : FleetDrone {
        attribute serial = "D7-0042";
        part pack : Battery {
            attribute :>> capacity = 5200.0;
            attribute :>> voltage = 22.2;
        }
        part autopilot : FlightController {
            attribute :>> loopRate = 400.0;
        }
        connection packPower connect pack.power to autopilot.power;
    }
}
