"""Recommendation service of the book store: hybrid (content + collaborative) neural model.

Keras runs on the PyTorch backend by default: sentence-transformers (the text embeddings) needs PyTorch
anyway, so only one framework is installed. The backend must be chosen before keras is first imported, and
any module of this package may import keras, so it is set here, when the package itself is imported. Set
KERAS_BACKEND=tensorflow (or jax) beforehand to use another one: the model code uses only keras.ops.
"""

import os

os.environ.setdefault("KERAS_BACKEND", "torch")
