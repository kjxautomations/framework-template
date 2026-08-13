namespace KJX.Scripting.Tests;

/// <summary>
/// Naming is shared verbatim between the analyzer, the generator and the runtime, so it is
/// pinned here rather than left to each of them.
/// </summary>
[TestFixture]
public class ScriptApiNamingTests
{
    [TestCase("ISyringePump", ExpectedResult = "syringe_pump")]
    [TestCase("ISensor", ExpectedResult = "sensor")]
    [TestCase("IStepperMotor", ExpectedResult = "stepper_motor")]
    [TestCase("ILed", ExpectedResult = "led")]
    [TestCase("Acquisition", ExpectedResult = "acquisition")]
    // A leading I is only dropped when a capital follows it, so IImageBuffer keeps its meaning
    // while Image does not lose its first letter.
    [TestCase("Image", ExpectedResult = "image")]
    [TestCase("IImageBuffer", ExpectedResult = "image_buffer")]
    // The convention cannot tell an interface prefix from a word that starts with I: IOExpander
    // reduces to o_expander, which is exactly what [ScriptApi("io_expander")] is for.
    [TestCase("IOExpander", ExpectedResult = "o_expander")]
    public string Derives_the_wire_type_name(string interfaceName) => ScriptApiNaming.WireTypeName(interfaceName);

    [TestCase("MoveTo", ExpectedResult = "move_to")]
    [TestCase("Prime", ExpectedResult = "prime")]
    [TestCase("StopAsync", ExpectedResult = "stop")]
    [TestCase("ReadSensor", ExpectedResult = "read_sensor")]
    [TestCase("Async", ExpectedResult = "async")]
    [TestCase("ReadHTTPHeader", ExpectedResult = "read_http_header")]
    public string Derives_the_method_name(string methodName) => ScriptApiNaming.MethodName(methodName);

    [TestCase("FlowRate", ExpectedResult = "get_flow_rate")]
    [TestCase("Position", ExpectedResult = "get_position")]
    public string Derives_the_getter_name(string propertyName) => ScriptApiNaming.PropertyGetterName(propertyName);

    [TestCase("FlowRate", ExpectedResult = "set_flow_rate")]
    public string Derives_the_setter_name(string propertyName) => ScriptApiNaming.PropertySetterName(propertyName);

    [TestCase("HTTPServer", ExpectedResult = "http_server")]
    [TestCase("IOPin", ExpectedResult = "io_pin")]
    [TestCase("Move_To", ExpectedResult = "move_to")]
    [TestCase("XAxis", ExpectedResult = "x_axis")]
    [TestCase("already_snake", ExpectedResult = "already_snake")]
    public string Converts_to_snake_case(string name) => ScriptApiNaming.ToSnakeCase(name);
}
