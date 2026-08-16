#pragma once

#include "happyphoton_libraw_bridge.h"
#include <libraw/libraw.h>

#include <atomic>
#include <memory>
#include <mutex>
#include <string>
#include <unordered_map>

namespace hplr {

enum class State { Open, Unpacked, Processed, Recycled };

struct Context {
    LibRaw raw;
    std::atomic<bool> busy{false};
    std::atomic<uint32_t> leases{0};
    std::atomic<uint32_t> allocations{0};
    std::atomic<uint64_t> active_lease{0};
    State state{State::Open};
};

struct Allocation {
    libraw_processed_image_t *image{};
    std::weak_ptr<Context> owner;
    bool registered{};
    ~Allocation();
};

extern std::mutex registry_mutex;
extern std::unordered_map<hplr_handle, std::shared_ptr<Context>> handles;
extern std::unordered_map<hplr_allocation, std::unique_ptr<Allocation>> allocations;
extern std::atomic<uint64_t> next_token;
#if !defined(HPLR_LIBRAW_REENTRANT)
extern std::mutex libraw_mutex;
#endif
#if defined(HPLR_TESTING)
extern std::atomic<bool> fail_next_allocation;
#endif

class Operation {
public:
    explicit Operation(std::shared_ptr<Context> value) : context_(std::move(value)) {}
    Operation(const Operation &) = delete;
    ~Operation() { if (locked_) context_->busy.store(false, std::memory_order_release); }
    bool try_lock() {
        bool expected = false;
        locked_ = context_->busy.compare_exchange_strong(expected, true,
            std::memory_order_acq_rel);
        return locked_;
    }
    Context &get() const { return *context_; }
    const std::shared_ptr<Context> &shared() const { return context_; }
private:
    std::shared_ptr<Context> context_;
    bool locked_{};
};

bool valid_error(hplr_error *error);
void clear_error(hplr_error *error);
int32_t fail(hplr_error *error, hplr_status status, hplr_error_class error_class,
             int native_code, const char *text);
int32_t libraw_fail(hplr_error *error, int code);
bool valid_header(const void *value, uint32_t abi, uint32_t size,
                  uint32_t expected_size, hplr_error *error);
std::shared_ptr<Context> find_context(hplr_handle handle);
int32_t begin(hplr_handle handle, std::unique_ptr<Operation> &operation, hplr_error *error);
uint64_t issue_token();
uint32_t copy_native_text(const char *source, size_t source_capacity,
                          uint8_t *destination, uint32_t destination_capacity);
int32_t validate_image(const libraw_processed_image_t *image, bool thumbnail,
                       hplr_error *error);

template<typename F>
int32_t boundary(hplr_error *error, F &&function) noexcept {
    try {
        if (!valid_error(error)) return HPLR_E_ARGUMENT;
        clear_error(error);
        return function();
    } catch (const std::bad_alloc &) {
        return fail(error, HPLR_E_RESOURCE, HPLR_ERROR_BRIDGE, 0,
                    "bridge allocation failed");
    } catch (...) {
        return fail(error, HPLR_E_INTERNAL, HPLR_ERROR_BRIDGE, 0,
                    "bridge caught a native exception");
    }
}

template<typename F>
decltype(auto) invoke_libraw(F &&function) {
#if defined(HPLR_LIBRAW_REENTRANT)
    return function();
#else
    std::lock_guard<std::mutex> lock(libraw_mutex);
    return function();
#endif
}

} // namespace hplr
