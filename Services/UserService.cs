using System;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens; 
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using WebApi.Entities;
using WebApi.Helpers;
using WebApi.Models;

namespace WebApi.Services
{
    public interface IUserService
    {
        AuthenticateResponse Authenticate(AuthenticateRequest model);
        bool Register(User model); 
        User GetById(int id); 
        List<User> GetAll();
    }
    public class UserService: IUserService
    {
        

        private readonly AppSettings _appSettings;
        private readonly WebApiDbcontext _webApiDbcontext;

        public UserService(IOptions<AppSettings> appSettings, WebApiDbcontext webApiDbcontext)
        {
            _appSettings = appSettings.Value;
            _webApiDbcontext = webApiDbcontext;
        }

        public AuthenticateResponse Authenticate(AuthenticateRequest model)
        {
            var user = _webApiDbcontext.User.SingleOrDefault(x => x.Username == model.Username && x.Password == model.Password); 
            if (user == null) return null;

            var token = generateJwtToken(user);

            return new AuthenticateResponse(user, token);
        }

        public List<User> GetAll()
        {
            return _webApiDbcontext.User.ToList(); 
        }

        public User GetById(int id)
        {
            return _webApiDbcontext.User.FirstOrDefault(x => x.Id == id);

        }

        public bool Register(User user)
        {
            bool isRegisted = false;
            using (var transaction = _webApiDbcontext.Database.BeginTransaction())
            {
                try
                {
                    if (user.Id > 0)
                    {
                        User UpdatedUser = GetById(user.Id);
                        UpdatedUser.FirstName = user.FirstName;
                        UpdatedUser.LastName = user.LastName;
                        UpdatedUser.Password = user.Password;
                    }
                    else
                    {
                        _webApiDbcontext.User.Add(user);
                    }
                    _webApiDbcontext.SaveChanges();
                transaction.Commit();
                isRegisted = true;
            }
                catch (Exception ex)
                {
                    isRegisted = false;
                }
            }
            return isRegisted;
        }


        // helper methods

        private string generateJwtToken(User user)
        {
            // generate token that is valid for 7 days
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(_appSettings.Secret);
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[] { new Claim("id", user.Id.ToString()) }),
                Expires = DateTime.UtcNow.AddDays(7),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };
            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }
    }
}
