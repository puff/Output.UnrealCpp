#include "pch.h"
#include "CppUnitTest.h"
#include "SDK.h"

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

#define CHEAT_GEAR_CHECK_OFFSET(targetClass, varName, expectedOffset) \
	Assert::AreEqual(uint32_t(expectedOffset), uint32_t(offsetof(targetClass, varName)), L#targetClass" -> "#varName".")

#define CHEAT_GEAR_CHECK_SIZE(targetClass, expectedSize) \
	Assert::AreEqual(uint32_t(expectedSize), uint32_t(sizeof(targetClass)), L#targetClass" Has a wrong size.")

namespace CheatGearCppUnitTests
{
	TEST_CLASS(CheatGear)
	{
	public:
/*!!CLASSES_ASSERT!!*/
	};
}
