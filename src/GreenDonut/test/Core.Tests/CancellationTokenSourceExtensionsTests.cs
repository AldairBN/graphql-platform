using System;
using 
            Assert.Equal(source.Token, combinedToken);
        }

        [Fact(DisplayName = "CreateLinkedCancellationToken: Should return a combined token")]
        public void CreateLinkedCancellationToken()
        {
            // arrange
            var source = new CancellationTokenSource();
            CancellationToken token = new CancellationTokenSource().Token;

            // act
            CancellationToken combinedToken = source
                .CreateLinkedCancellationToken(token);

            // assert
            Assert.NotEqual(source.Token, combinedToken);
            Assert.NotEqual(token, combinedToken);
        }
    }
}
