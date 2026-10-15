#include <CppUnitTest.h>
#include "Temperature.h"

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace TemperatureConverterCppTests
{
    TEST_CLASS(TemperatureTests)
    {
    public:
        TEST_METHOD(FreezingPointIs32Fahrenheit)
        {
            Assert::AreEqual(32.0, temperature::ToFahrenheit(0), 1e-10);
        }

        TEST_METHOD(BoilingPointIs212Fahrenheit)
        {
            Assert::AreEqual(212.0, temperature::ToFahrenheit(100), 1e-10);
        }

        TEST_METHOD(MinusFortyIsTheSameInBothScales)
        {
            Assert::AreEqual(-40.0, temperature::ToFahrenheit(-40), 1e-10);
        }

        TEST_METHOD(BodyTemperatureIsConvertedToFahrenheit)
        {
            Assert::AreEqual(97.88, temperature::ToFahrenheit(36.6), 1e-10);
        }
    };
}
