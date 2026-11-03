/*
 * Who uses the drone, what they say to one another, and how its life goes.
 */
package DroneInteractions {
    private import DroneLifecycle::*;
    private import DroneStructure::*;
    private import ScalarValues::*;

    part def Customer;
    part def Operator;
    item def Order;
    item def Clearance;
    item def Thrust;
    item def Parcel;

    // ----- use cases: who the drone is for, inside its system boundary -----

    use case def CheckWeather {
        subject drone : Drone;
        actor operator : Operator;
    }

    use case def PlanRoute {
        subject drone : Drone;
    }

    use case def DropParcel {
        subject drone : Drone;
        actor customer : Customer;
    }

    use case def DeliverParcel {
        doc /* A customer orders, an operator clears it, the drone flies it. */
        subject drone : Drone;
        actor customer : Customer;
        actor operator : Operator;
        include use case weather : CheckWeather;
        include use case route : PlanRoute;
        include use case drop : DropParcel;
    }

    use case def ReturnHome {
        subject drone : Drone;
        actor operator : Operator;
        include use case route : PlanRoute;
    }

    // ----- interactions: the same parts, in the order things are said -----

    occurrence def RequestClearance {
        doc /* An operator asks for a flight and the drone checks it can go. */
        part operator : Operator {
            event occurrence sendOrder;
            then event occurrence receiveClearance;
        }
        part fc : FlightController {
            action planFlight {
                event occurrence receiveOrder;
                then event occurrence sendQuery;
                then event occurrence receiveFix;
                then event occurrence sendClearance;
            }
        }
        part gps : Gps {
            event occurrence receiveQuery;
            then event occurrence sendFix;
        }
        message order of Order from operator.sendOrder to fc.planFlight.receiveOrder;
        then message queryFix from fc.planFlight.sendQuery to gps.receiveQuery;
        then message fix from gps.sendFix to fc.planFlight.receiveFix;
        then message clearance of Clearance from fc.planFlight.sendClearance to operator.receiveClearance;
    }

    occurrence def FlyToAddress {
        doc /* The flight controller steers: thrust out, attitude back. */
        part fc : FlightController {
            event selfCheck.sourceEvent;
            then event selfCheck.targetEvent;
            then event sendThrust.sourceEvent;
            then event attitude.targetEvent;
        }
        part motors : Motor[4] {
            event sendThrust.targetEvent;
        }
        part imu : Imu {
            event attitude.sourceEvent;
            then event occurrence calibrated;
        }
        message selfCheck;
        then message sendThrust of Thrust;
        then message attitude;
    }

    occurrence def HandOver {
        doc /* The parcel changes hands at the door. */
        part bay : PayloadBay;
        part customer : Customer;
        succession flow parcel of Parcel from bay to customer;
        message receipt from customer to bay;
    }

    // ----- interaction overview: the interactions as steps of one activity -----

    action def DeliveryOverview {
        doc /* The delivery, one interaction at a time. */
        first start;
        then occurrence clearance : RequestClearance;
        then decide cleared;
            if isCleared then flight;
            else refuse;
        occurrence flight : FlyToAddress;
        then occurrence handOver : HandOver;
        first handOver then done;
        action refuse;
        then done;
    }

    // ----- timing: one flight's life along what comes after what -----

    individual def DeliveryFlight {
        doc /* The model orders these and gives no durations, so neither does the diagram. */
        individual battery : Battery {
            snapshot full { attribute charge = 100; }
            then timeslice discharging;
            then snapshot low { attribute charge = 20; }
            then timeslice recharging;
        }
        individual controller : FlightController {
            timeslice armed;
            then timeslice cruising;
            then timeslice landing;
            then snapshot parked;
        }
        first battery.low then controller.landing;
    }
}
