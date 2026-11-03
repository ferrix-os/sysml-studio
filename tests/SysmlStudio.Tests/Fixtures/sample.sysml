/*
 * A small model that exercises every shape the indexer recognises.
 */
package SampleLifecycle {
    metadata def implemented;
    metadata def planned;
}

package Sample {
    private import ScalarValues::*;
    private import SampleLifecycle::*;

    part def <'P.1'> Engine {
        doc /* Turns fuel into torque. */
        attribute power : Real;
        port shaft : ShaftPort;
    }

    port def ShaftPort;

    #implemented
    part def Vehicle {
        part engine : Engine;
        part wheels : Wheel[4];
        connect engine.shaft to wheels;
    }

    #planned
    part def Wheel :> RollingThing;

    part def RollingThing;

    requirement def <'R.1'> MustStart {
        doc /* The vehicle starts within two seconds. */
    }

    satisfy MustStart by Vehicle::engine;

    requirement def MustStopToo;
    dependency from MustStopToo to MustStart;

    action def Boot {
        first start;
        then action check;
        then action load;
        then done;
    }

    action def StartUp {
        action crank;
        action idle;
        first crank then idle;
    }

    state def Running {
        entry; then off;
        state off;
        transition off_to_on
            first off
            then on;
        state on;
    }
}

package SampleBehaviour {
    private import Sample::*;

    part def Driver;
    part def Pump;
    item def Fuel;

    use case def PayForFuel {
        subject vehicle : Vehicle;
    }

    use case def Refuel {
        subject vehicle : Vehicle;
        actor driver : Driver;
        include use case pay : PayForFuel;
    }

    action def Check {
        first start;
        then merge again;
        then action measure;
        then decide;
            if tooLow then fill;
            else go;
        action fill;
        then again;
        action go;
        then fork;
            then left;
            then right;
        action left;
        then joined;
        action right;
        then joined;
        join joined;
        then done;
    }

    state def Tank {
        entry; then empty;
        state empty {
            entry action closeValve;
            do action watchLevel;
            exit action openValve;
        }
        state filling {
            entry; then slow;
            state slow;
            state fast;
            transition speedUp first slow then fast;
        }
        transition open first empty accept nozzle if pumpOn do action startMeter then filling;
        transition overflow first filling then filling;
        transition sealed first filling then done;
    }

    state def Cooling parallel {
        state fan {
            state fanOff;
        }
        state pump {
            state pumpOff;
        }
    }

    occurrence def Handshake {
        part driver : Driver {
            event occurrence ask;
            then event occurrence hear;
        }
        part pump : Pump {
            action serve {
                event occurrence listen;
                then event occurrence meter;
                then event occurrence answer;
            }
            event occurrence idle;
        }
        message reply from pump.serve.answer to driver.hear;
        first hello then count;
        message hello of Fuel from driver.ask to pump.serve.listen;
        message count from pump.serve.meter to pump;
        first count then reply;
    }

    occurrence def Silent {
        part a : Driver {
            event ping.sourceEvent;
        }
        part b : Pump {
            event ping.targetEvent;
        }
        message ping;
        then message of litres : Fuel from a to b;
    }

    action def Overview {
        first start;
        then occurrence greet : Handshake;
        then action pause;
        then occurrence greetAgain : Handshake;
        first greetAgain then done;
    }

    individual def OneTank {
        individual gauge : Pump {
            snapshot full { attribute level = 100; }
            then timeslice draining;
            then snapshot dry;
        }
        individual tap : Pump {
            timeslice shut;
            then timeslice running;
        }
        first gauge.dry then tap.running;
    }

    part def NothingHappens {
        part a : Driver;
        part b : Pump;
    }
}

package Broken {
    private import Sample::*;
    part def UsesMissing {
        part x : Sample::NoSuchThing;
    }
}
