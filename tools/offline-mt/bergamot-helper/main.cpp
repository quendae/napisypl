#include <filesystem>
#include <iostream>
#include <memory>
#include <optional>
#include <string>
#include <vector>

#include <nlohmann/json.hpp>

#include "translator/parser.h"
#include "translator/response_options.h"
#include "translator/service.h"
#include "translator/translation_model.h"

using json = nlohmann::json;
namespace bergamot = marian::bergamot;

namespace {

void emit(const json &value) {
  std::cout << value.dump() << '\n' << std::flush;
}

void emitError(const std::optional<std::string> &jobId, const std::string &code, const std::string &message) {
  json response = {
      {"type", "error"},
      {"jobId", jobId ? json(*jobId) : json(nullptr)},
      {"code", code},
      {"message", message},
  };
  emit(response);
}

void diagnostic(const std::string &message) {
  std::cerr << "[SubFlow.BergamotHelper] " << message << '\n' << std::flush;
}

std::filesystem::path resolveConfigPath(const std::string &modelPath) {
  const std::filesystem::path path(modelPath);
  if (std::filesystem::is_regular_file(path)) {
    return path;
  }
  return path / "config.yml";
}

}  // namespace

int main() {
  std::ios::sync_with_stdio(false);

  std::unique_ptr<bergamot::BlockingService> service;
  std::shared_ptr<bergamot::TranslationModel> model;
  std::string line;

  while (std::getline(std::cin, line)) {
    json command;
    try {
      command = json::parse(line);
    } catch (...) {
      emitError(std::nullopt, "invalid_command", "Invalid JSON command.");
      continue;
    }

    if (!command.is_object() || !command.contains("type") || !command["type"].is_string()) {
      emitError(std::nullopt, "invalid_command", "Command type is required.");
      continue;
    }

    const std::string type = command["type"].get<std::string>();
    if (type == "shutdown") {
      return 0;
    }

    if (type == "load") {
      try {
        if (!command.contains("modelPath") || !command["modelPath"].is_string()) {
          emitError(std::nullopt, "invalid_command", "modelPath is required.");
          continue;
        }

        const auto configPath = resolveConfigPath(command["modelPath"].get<std::string>());
        if (!std::filesystem::is_regular_file(configPath)) {
          emitError(std::nullopt, "model_load_failed", "Bergamot config.yml was not found.");
          continue;
        }

        diagnostic("loading model config: " + configPath.string());
        bergamot::BlockingService::Config serviceConfig;
        serviceConfig.cacheSize = 0;
        serviceConfig.logger.level = "info";
        auto modelConfig = bergamot::parseOptionsFromFilePath(configPath.string());

        auto nextService = std::make_unique<bergamot::BlockingService>(serviceConfig);
        auto nextModel = std::make_shared<bergamot::TranslationModel>(modelConfig);

        service = std::move(nextService);
        model = std::move(nextModel);
        diagnostic("model loaded");
        emit({{"type", "ready"}, {"model", configPath.parent_path().filename().string()}, {"version", "0.6.0"}});
      } catch (const std::exception &ex) {
        diagnostic(std::string("model load exception: ") + ex.what());
        service.reset();
        model.reset();
        emitError(std::nullopt, "model_load_failed", "Bergamot model could not be loaded.");
      } catch (...) {
        diagnostic("model load exception: unknown");
        service.reset();
        model.reset();
        emitError(std::nullopt, "model_load_failed", "Bergamot model could not be loaded.");
      }
      continue;
    }

    if (type == "translate") {
      std::optional<std::string> jobId;
      try {
        if (!command.contains("jobId") || !command["jobId"].is_string() ||
            !command.contains("segments") || !command["segments"].is_array()) {
          emitError(std::nullopt, "invalid_command", "jobId and segments are required.");
          continue;
        }

        jobId = command["jobId"].get<std::string>();
        if (!service || !model) {
          emitError(jobId, "model_not_loaded", "Load a Bergamot model before translation.");
          continue;
        }

        std::vector<int> ids;
        std::vector<std::string> texts;
        ids.reserve(command["segments"].size());
        texts.reserve(command["segments"].size());

        for (const auto &segment : command["segments"]) {
          if (!segment.is_object() || !segment.contains("id") || !segment["id"].is_number_integer() ||
              !segment.contains("text") || !segment["text"].is_string()) {
            emitError(jobId, "invalid_command", "Each segment requires integer id and string text.");
            ids.clear();
            break;
          }
          ids.push_back(segment["id"].get<int>());
          texts.push_back(segment["text"].get<std::string>());
        }
        if (ids.empty() && !command["segments"].empty()) {
          continue;
        }
        if (ids.empty()) {
          emitError(jobId, "invalid_command", "At least one segment is required.");
          continue;
        }

        diagnostic("translateMultiple begin; segments=" + std::to_string(ids.size()));
        std::vector<bergamot::ResponseOptions> responseOptions(ids.size());
        auto responses = service->translateMultiple(model, std::move(texts), responseOptions);
        diagnostic("translateMultiple returned; responses=" + std::to_string(responses.size()));
        if (responses.size() != ids.size()) {
          emitError(jobId, "translation_failed", "Bergamot returned an unexpected result count.");
          continue;
        }

        for (std::size_t i = 0; i < responses.size(); ++i) {
          emit({{"type", "segment"}, {"jobId", *jobId}, {"id", ids[i]}, {"text", responses[i].target.text}});
          emit({{"type", "progress"}, {"jobId", *jobId}, {"completed", i + 1}, {"total", responses.size()}});
        }
        emit({{"type", "complete"}, {"jobId", *jobId}});
      } catch (const std::exception &ex) {
        diagnostic(std::string("translation exception: ") + ex.what());
        emitError(jobId, "translation_failed", "Bergamot translation failed.");
      } catch (...) {
        diagnostic("translation exception: unknown");
        emitError(jobId, "translation_failed", "Bergamot translation failed.");
      }
      continue;
    }

    emitError(std::nullopt, "invalid_command", "Unknown command type.");
  }

  return 0;
}
