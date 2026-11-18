"""The hybrid recommendation model, written in Keras 3.

Keras runs on the PyTorch backend by default: sentence-transformers (the text embeddings) needs PyTorch
anyway, so only one framework is installed. The backend must be chosen before keras is first imported; set
KERAS_BACKEND=tensorflow (or jax) beforehand to use another one: the model code uses only keras.ops.
"""

import os

os.environ.setdefault("KERAS_BACKEND", "torch")
