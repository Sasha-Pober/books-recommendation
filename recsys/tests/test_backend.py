import os
import subprocess
import sys


def test_service_modules_select_the_pytorch_backend_by_themselves() -> None:
    """The service imports keras outside recsys.model (e.g. inference.py); TensorFlow isn't installed.

    Runs in a fresh interpreter without KERAS_BACKEND (the test session sets it in conftest).
    """
    env = {k: v for k, v in os.environ.items() if k != "KERAS_BACKEND"}
    code = "import recsys.inference, keras; print(keras.backend.backend())"

    result = subprocess.run([sys.executable, "-c", code], env=env, capture_output=True, text=True, timeout=300)

    assert result.returncode == 0, result.stderr[-2000:]
    assert result.stdout.strip().splitlines()[-1] == "torch"
