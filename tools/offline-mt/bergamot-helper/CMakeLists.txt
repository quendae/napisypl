cmake_minimum_required(VERSION 3.15)

include(FetchContent)

FetchContent_Declare(
  nlohmann_json
  GIT_REPOSITORY https://github.com/nlohmann/json.git
  GIT_TAG 9cca280a4d0ccf0c08f47a99aa71d1b0e52f8d03
  GIT_SHALLOW FALSE
)
FetchContent_MakeAvailable(nlohmann_json)

add_executable(SubFlow.BergamotHelper main.cpp)
target_link_libraries(SubFlow.BergamotHelper PRIVATE bergamot-translator nlohmann_json::nlohmann_json)
target_include_directories(SubFlow.BergamotHelper PRIVATE "${PROJECT_SOURCE_DIR}/src")
target_compile_features(SubFlow.BergamotHelper PRIVATE cxx_std_17)
