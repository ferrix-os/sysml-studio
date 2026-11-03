/*
 * The flight software, and which part of the drone each piece runs on.
 */
package DroneSoftware {
    private import DroneStructure::*;
    private import DroneBehaviour::*;
    public import DroneSoftware::Flight::*;

    package Flight {
        #implemented
        part def AttitudeLoop;

        #inProgress
        part def Navigator;
    }

    package Sensing {
        part def ImuDriver;
    }

    allocation def RunsOn;

    allocation attitudeOnFc : RunsOn allocate Flight::AttitudeLoop to Drone::fc;
    allocate Flight::Navigator to Drone::fc;
    allocate Sensing::ImuDriver to Drone::imu;
    allocate Deliver to Flight::Navigator;
}
