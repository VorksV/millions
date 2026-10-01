cmake_minimum_required(VERSION 3.10)

project(RecoveryEngine VERSION 1.0 LANGUAGES CXX C)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED True)

# Create shared library (DLL)
add_library(recovery_engine SHARED
    dllmain.cpp
    Scanner.cpp
    NTFSParser.cpp
    UsnJournal.cpp
    RecoveryEngine.h
    Scanner.h
    NTFSParser.h
    FileSignatures.h
)

# Optional: Add specific compiler flags for performance
if(MSVC)
    target_compile_options(recovery_engine PRIVATE /O2 /Oi /GL)
    set_target_properties(recovery_engine PROPERTIES LINK_FLAGS "/LTCG")
    
    # Differentiate 32-bit vs 64-bit output names automatically
    if(CMAKE_SIZEOF_VOID_P EQUAL 8)
        set_target_properties(recovery_engine PROPERTIES OUTPUT_NAME "recovery_engine_x64")
    else()
        set_target_properties(recovery_engine PROPERTIES OUTPUT_NAME "recovery_engine_x86")
    endif()
endif()

# We only need standard Windows libraries which are linked by default.
