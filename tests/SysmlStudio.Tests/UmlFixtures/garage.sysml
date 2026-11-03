/*
 * A small model for the structural UML views, one of each thing they draw.
 */
package Parts {
    part def Bolt;
}

package Empty {
    private import ScalarValues::*;
    part def Lonely;
}

package Garage {
    private import ScalarValues::*;
    private import SysML::*;
    public import Parts::*;
    private import Helpers::*;

    package Tools {
        part def Jack;
    }

    metadata def Safety {
        attribute level : Integer;
        :> annotatedElement : SysML::ItemDefinition;
    }

    metadata def Reviewed;

    port def Mains {
        out attribute amps : Real;
    }

    abstract part def Machine {
        attribute serial : String;
        action start {
            in key : String;
            out ok : Boolean;
        }
    }

    enum def Fuel {
        enum petrol;
        enum diesel;
    }

    #Safety
    part def Lift :> Machine {
        attribute load : Real;
        private attribute hours : Real;
        port supply : ~Mains;
        ref owner : Mechanic;
        part bolts : Parts::Bolt[8];
        part arm : Arm;
    }

    part def Arm;

    #Reviewed
    part def Mechanic;

    @Safety about Mechanic { level = 2; }

    part def Socket {
        port out1 : Mains;
        port spare : Mains;
    }

    part def Workshop {
        part lift : Lift;
        part socket : Socket;
        ref boss : Mechanic;
        port door : Mains;
        connection feed connect socket.out1 to lift.supply;
    }

    part def Quiet {
        attribute a : Real;
    }

    allocate Tools::Jack to Workshop::lift;
    allocation armOn allocate Arm to Workshop::lift;

    individual part def MyLift :> Lift;

    individual part mine : MyLift {
        attribute :>> load = 2.5;
        part hoist : Arm {
            attribute :>> serial = "H-1";
        }
        ref keeper = alice;
    }

    individual part alice : Mechanic;

    part configured : Lift {
        attribute :>> load = 1.0;
    }
}

package Helpers {
    part def Rag;
}
