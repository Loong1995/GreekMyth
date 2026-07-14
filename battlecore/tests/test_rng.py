import _path_bootstrap  # noqa: F401

from battlecore.rng.deterministic_rng import DeterministicRNG
from _output_helper import format_rng_history, print_and_save_output


def test_rng_is_deterministic() -> None:
    a = DeterministicRNG(12345)
    b = DeterministicRNG(12345)

    assert [a.rand_bps("test", "roll") for _ in range(10)] == [
        b.rand_bps("test", "roll") for _ in range(10)
    ]
    print_and_save_output("test_rng_is_deterministic", format_rng_history("RNG Deterministic Rolls", a))

    assert a.index == 10
    assert all(0 <= item["roll_bps"] <= 9999 for item in a.history)


if __name__ == "__main__":
    import pytest

    raise SystemExit(pytest.main([__file__]))
