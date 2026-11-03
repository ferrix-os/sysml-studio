/*
 * The maturity keywords the other files use.
 */
package DroneLifecycle {
    private import ScalarValues::*;
    private import SysML::*;

    metadata def implemented;
    metadata def inProgress;
    metadata def planned;

    metadata def Safety {
        attribute level : Integer;
        :> annotatedElement : SysML::PartDefinition;
    }
}
