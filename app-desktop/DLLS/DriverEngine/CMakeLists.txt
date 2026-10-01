cmake_minimum_required(VERSION 3.10)

project(DriverEngine VERSION 1.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED True)

# Create shared library (DLL)
add_library(driver_engine SHARED
    DriverEngine.cpp
    DriverEngine.h
)

target_compile_definitions(driver_engine PRIVATE
    DRIVERENGINE_EXPORTS
    UNICODE
    _UNICODE
    WIN32_LEAN_AND_MEAN
    NOMINMAX
    _WIN32_WINNT=0x0A00
)

if(MSVC)
    target_compile_options(driver_engine PRIVATE /O2 /Oi /GL /W3 /WX-)
    set_target_properties(driver_engine PROPERTIES LINK_FLAGS "/LTCG")

    # Differentiate 32-bit vs 64-bit output names automatically
    if(CMAKE_SIZEOF_VOID_P EQUAL 8)
        set_target_properties(driver_engine PROPERTIES OUTPUT_NAME "driver_engine_x64")
    else()
        set_target_properties(driver_engine PROPERTIES OUTPUT_NAME "driver_engine_x86")
    endif()
endif()

# Link required Windows SDK libraries
target_link_libraries(driver_engine PRIVATE
    setupapi
    cfgmgr32
    wintrust
    crypt32
    shlwapi
    newdev
    advapi32
    shell32
    version
)
