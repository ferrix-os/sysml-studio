/*
 * How a delivery flight goes, and the modes the flight controller moves through.
 */
package DroneBehaviour {
    private import DroneLifecycle::*;
    private import ScalarValues::*;

    item def Goal;
    item def GoAround;

    action def Deliver {
        action preflightCheck;
        action takeOff;
        action cruise;
        action descend;
        action releaseParcel;
        action returnHome;
        action land;
        first preflightCheck then takeOff;
        first takeOff then cruise;
        first cruise then descend;
        first descend then releaseParcel;
        first releaseParcel then returnHome;
        first returnHome then land;
    }

    action def PlanFlight {
        doc /* Decide whether to go, then check the weather and the battery at once. */
        in attribute windy : Boolean;
        first start;
        then merge retry;
        then action fetchForecast;
        then decide flyable;
            if windy then waitForWind;
            else ready;
        action waitForWind;
        then retry;
        action ready;
        then fork;
            then checkBattery;
            then checkRoute;
        action checkBattery;
        then allChecked;
        action checkRoute;
        then allChecked;
        join allChecked;
        then action announce send Goal();
        then action confirm accept GoAround;
        then done;
    }

    state def FlightModes {
        entry; then disarmed;
        state disarmed {
            entry action lockMotors;
            exit action spinUp;
        }
        state armed;
        state flying {
            do action holdCourse;
            entry; then cruising;
            state cruising;
            state hovering;
            transition hold first cruising then hovering;
            transition resume first hovering then cruising;
        }
        state landing {
            do action descendSlowly;
        }
        state failsafe;
        transition arm first disarmed then armed;
        transition launch first armed accept go if batteryOk do action logLaunch then flying;
        transition descend first flying then landing;
        transition touchdown first landing then disarmed;
        transition linkLost first flying accept lostLink then failsafe;
        transition recover first failsafe then landing;
        transition retire first disarmed if worn then done;
    }

    state def Airframe {
        entry; then idle;
        state idle;
        state watching parallel {
            state lights {
                entry; then steady;
                state steady;
                state flashing;
                transition warn first steady then flashing;
            }
            state radio {
                entry; then listening;
                state listening;
            }
        }
        transition wake first idle then watching;
    }
}
